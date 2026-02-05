using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ChloyeDesktop.Services;

namespace ChloyeDesktop.Bridge;

public class BridgeHandler
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BridgeHandler> _logger;
    private readonly ConversationService _conversations;
    private readonly ChatService _chat;
    private readonly SecretsService _secrets;
    private readonly McpManager _mcp;
    private CancellationTokenSource? _streamCts;
    
    // Callback to send streaming events to UI
    public Action<string>? OnStreamEvent { get; set; }

    public BridgeHandler(IServiceProvider services)
    {
        _services = services;
        _logger = services.GetRequiredService<ILogger<BridgeHandler>>();
        _conversations = services.GetRequiredService<ConversationService>();
        _chat = services.GetRequiredService<ChatService>();
        _secrets = services.GetRequiredService<SecretsService>();
        _mcp = services.GetRequiredService<McpManager>();
    }

    public async Task<string> HandleMessage(string messageJson)
    {
        BridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(messageJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse bridge message");
            return CreateErrorResponse(null, "Invalid message format");
        }

        if (request == null)
        {
            return CreateErrorResponse(null, "Empty request");
        }

        try
        {
            var result = await DispatchMethod(request.Method, request.Params);
            return CreateSuccessResponse(request.Id, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling method: {Method}", request.Method);
            return CreateErrorResponse(request.Id, ex.Message);
        }
    }

    private async Task<object?> DispatchMethod(string method, JsonElement? parameters)
    {
        return method switch
        {
            // Conversations
            "conversations.list" => _conversations.ListConversations(),
            "conversations.create" => _conversations.CreateConversation(
                parameters?.GetProperty("title").GetString() ?? "New Conversation"),
            "conversations.delete" => _conversations.DeleteConversation(
                Guid.Parse(parameters?.GetProperty("id").GetString()!)),
            "conversations.rename" => _conversations.RenameConversation(
                Guid.Parse(parameters?.GetProperty("id").GetString()!),
                parameters?.GetProperty("title").GetString()!),

            // Messages
            "messages.list" => _conversations.GetMessages(
                Guid.Parse(parameters?.GetProperty("conversationId").GetString()!)),
            "messages.send" => await HandleSendMessage(parameters),
            "messages.stopStream" => StopStream(),

            // Settings
            "settings.hasApiKey" => _secrets.HasSecret("OpenAI"),
            "settings.setApiKey" => SetApiKey(parameters?.GetProperty("key").GetString()!),
            "settings.clearApiKey" => _secrets.DeleteSecret("OpenAI"),
            "settings.testOpenAI" => await _chat.TestConnectionAsync(),

            // MCP Servers
            "mcp.list" => ListMcpServersWithStatus(),
            "mcp.configPath" => _mcp.GetConfigFilePath(),
            "mcp.openConfig" => OpenMcpConfigFile(),
            "mcp.start" => await _mcp.StartServer(
                Guid.Parse(parameters?.GetProperty("id").GetString()!)),
            "mcp.stop" => StopMcpServer(parameters),
            "mcp.logs" => _mcp.GetLogs(
                Guid.Parse(parameters?.GetProperty("id").GetString()!),
                parameters?.TryGetProperty("maxLines", out var ml) == true ? ml.GetInt32() : 100),

            // MCP Tools
            "mcp.tools.list" => await _mcp.ListToolsAsync(
                Guid.Parse(parameters?.GetProperty("serverId").GetString()!)),
            "mcp.tools.call" => await _mcp.CallToolAsync(
                Guid.Parse(parameters?.GetProperty("serverId").GetString()!),
                parameters?.GetProperty("toolName").GetString()!,
                parameters?.GetProperty("args") ?? default),
            "mcp.allTools" => await GetAllMcpTools(),

            _ => throw new NotSupportedException($"Unknown method: {method}")
        };
    }

    private async Task<object> HandleSendMessage(JsonElement? parameters)
    {
        var conversationId = Guid.Parse(parameters?.GetProperty("conversationId").GetString()!);
        var content = parameters?.GetProperty("content").GetString()!;
        var model = parameters?.GetProperty("model").GetString() ?? "gpt-5.2";
        var reasoningEffort = parameters?.TryGetProperty("reasoningEffort", out var effortProp) == true 
            ? effortProp.GetString() ?? "medium" 
            : "medium";

        // Save user message
        var userMessage = _conversations.AddMessage(conversationId, "user", content, model);

        // Create assistant message placeholder
        var assistantMessage = _conversations.AddMessage(conversationId, "assistant", "", model);

        // Get conversation history for context
        var messages = _conversations.GetMessages(conversationId);
        var chatMessages = messages
            .Where(m => m.Id != assistantMessage.Id)
            .Select(m => new ChatMessage { Role = m.Role, Content = m.Content })
            .ToList();

        // Fetch tools from all connected MCP servers
        var mcpTools = await _mcp.GetAllToolsAsync();
        var openAiTools = mcpTools.Select(t => ConvertToOpenAiFunction(t.Tool)).ToList();
        _logger.LogInformation("Loaded {ToolCount} tools from {ServerCount} MCP servers", 
            openAiTools.Count, mcpTools.Select(t => t.ServerId).Distinct().Count());

        var request = new ChatRequest
        {
            Model = model,
            ReasoningEffort = reasoningEffort,
            Messages = chatMessages,
            Tools = openAiTools.Count > 0 ? openAiTools : null
        };

        _streamCts = new CancellationTokenSource();
        var fullContent = new System.Text.StringBuilder();

        // Send initial response with message IDs so UI can start showing them
        SendStreamEvent("stream.start", new
        {
            messageId = assistantMessage.Id.ToString(),
            conversationId = conversationId.ToString()
        });

        try
        {
            // Tool calling loop - continue until we get a final text response
            const int maxToolCalls = 10; // Prevent infinite loops
            var toolCallCount = 0;

            while (toolCallCount < maxToolCalls)
            {
                ToolCallInfo? pendingToolCall = null;

                await foreach (var chunk in _chat.StreamCompletionAsync(request, _streamCts.Token))
                {
                    if (chunk.Done) break;
                    
                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        fullContent.Append(chunk.Content);
                        // Send each chunk to UI
                        SendStreamEvent("stream.delta", new
                        {
                            messageId = assistantMessage.Id.ToString(),
                            delta = chunk.Content
                        });
                    }
                    
                    // Check for tool call
                    if (chunk.ToolCall != null)
                    {
                        pendingToolCall = chunk.ToolCall;
                    }
                }

                // If there's a pending tool call, execute it
                if (pendingToolCall != null)
                {
                    toolCallCount++;
                    _logger.LogInformation("Executing tool call {Count}: {ToolName}", toolCallCount, pendingToolCall.Name);

                    // Notify UI about tool call
                    var toolCallText = $"\n\n🔧 *Calling tool: **{pendingToolCall.Name}***\n";
                    fullContent.Append(toolCallText);
                    SendStreamEvent("stream.delta", new
                    {
                        messageId = assistantMessage.Id.ToString(),
                        delta = toolCallText
                    });

                    // Execute the tool via MCP
                    string toolResultText;
                    try
                    {
                        var argsJson = JsonDocument.Parse(pendingToolCall.Arguments).RootElement;
                        var result = await _mcp.CallToolByNameAsync(pendingToolCall.Name, argsJson);
                        
                        // Extract text from result
                        toolResultText = ExtractToolResultText(result);
                        _logger.LogDebug("Tool result: {Result}", toolResultText.Substring(0, Math.Min(200, toolResultText.Length)));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Tool call failed: {ToolName}", pendingToolCall.Name);
                        toolResultText = $"Error: {ex.Message}";
                    }

                    // Add tool result notification to UI
                    var resultNotice = $"\n*Tool result received*\n\n";
                    fullContent.Append(resultNotice);
                    SendStreamEvent("stream.delta", new
                    {
                        messageId = assistantMessage.Id.ToString(),
                        delta = resultNotice
                    });

                    // Add assistant's tool call message and tool result to conversation
                    chatMessages.Add(new ChatMessage { Role = "assistant", Content = $"[Called {pendingToolCall.Name}]" });
                    chatMessages.Add(new ChatMessage { Role = "user", Content = $"Tool result for {pendingToolCall.Name}:\n{toolResultText}" });

                    // Update request for next iteration
                    request = new ChatRequest
                    {
                        Model = model,
                        ReasoningEffort = reasoningEffort,
                        Messages = chatMessages,
                        Tools = openAiTools.Count > 0 ? openAiTools : null
                    };

                    // Continue the loop to get the next response
                    continue;
                }

                // No tool call means we're done
                break;
            }

            if (toolCallCount >= maxToolCalls)
            {
                _logger.LogWarning("Reached maximum tool call limit ({Max})", maxToolCalls);
                fullContent.Append("\n\n*Maximum tool calls reached*");
            }

            _conversations.UpdateMessageContent(assistantMessage.Id, fullContent.ToString());
            
            // Signal stream complete
            SendStreamEvent("stream.done", new
            {
                messageId = assistantMessage.Id.ToString()
            });
        }
        catch (OperationCanceledException)
        {
            _conversations.UpdateMessageContent(assistantMessage.Id, fullContent.ToString());
            SendStreamEvent("stream.done", new
            {
                messageId = assistantMessage.Id.ToString()
            });
        }

        return new
        {
            userMessage,
            assistantMessage = new
            {
                assistantMessage.Id,
                assistantMessage.ConversationId,
                assistantMessage.Role,
                Content = fullContent.ToString(),
                assistantMessage.Model,
                assistantMessage.CreatedAt
            }
        };
    }

    /// <summary>
    /// Converts an MCP tool definition to OpenAI function calling format
    /// </summary>
    private object ConvertToOpenAiFunction(McpTool tool)
    {
        // OpenAI function calling format
        return new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description ?? "",
                parameters = tool.InputSchema.HasValue 
                    ? JsonSerializer.Deserialize<object>(tool.InputSchema.Value.GetRawText())
                    : new { type = "object", properties = new { } }
            }
        };
    }

    /// <summary>
    /// Extracts text content from an MCP tool result
    /// </summary>
    private string ExtractToolResultText(JsonElement result)
    {
        // MCP tool results typically have a "content" array with text items
        if (result.TryGetProperty("content", out var contentArray) && 
            contentArray.ValueKind == JsonValueKind.Array)
        {
            var textParts = new List<string>();
            foreach (var item in contentArray.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var typeProp) && 
                    typeProp.GetString() == "text" &&
                    item.TryGetProperty("text", out var textProp))
                {
                    textParts.Add(textProp.GetString() ?? "");
                }
            }
            if (textParts.Count > 0)
            {
                return string.Join("\n", textParts);
            }
        }

        // Fallback: return the raw JSON
        return result.GetRawText();
    }

    
    private void SendStreamEvent(string eventType, object data)
    {
        if (OnStreamEvent == null)
        {
            _logger.LogWarning("OnStreamEvent callback is null, cannot send event: {EventType}", eventType);
            return;
        }
        
        var eventJson = JsonSerializer.Serialize(new
        {
            type = eventType,
            data
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        
        _logger.LogDebug("Sending stream event: {EventType} - {Json}", eventType, eventJson);
        OnStreamEvent(eventJson);
    }

    private bool StopStream()
    {
        _streamCts?.Cancel();
        return true;
    }

    private bool SetApiKey(string key)
    {
        _secrets.SetSecret("OpenAI", key);
        return true;
    }

    private object ListMcpServersWithStatus()
    {
        var servers = _mcp.ListServers();
        return servers.Select(s => new
        {
            s.Id,
            s.Name,
            s.Type,
            s.Command,
            s.ArgsJson,
            s.Cwd,
            s.EnvJson,
            s.AutoStart,
            s.Url,
            s.AuthType,
            s.AuthRef,
            s.CreatedAt,
            s.UpdatedAt,
            Status = _mcp.GetStatus(s.Id).ToString().ToLower()
        }).ToList();
    }

    private async Task<object> GetAllMcpTools()
    {
        var tools = await _mcp.GetAllToolsAsync();
        return tools.Select(t => new
        {
            serverId = t.ServerId.ToString(),
            serverName = t.ServerName,
            name = t.Tool.Name,
            description = t.Tool.Description,
            inputSchema = t.Tool.InputSchema
        }).ToList();
    }

    private bool OpenMcpConfigFile()
    {
        var configPath = _mcp.GetConfigFilePath();
        
        // Create config file with example if it doesn't exist
        if (!System.IO.File.Exists(configPath))
        {
            var dir = System.IO.Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            
            var exampleConfig = @"{
  ""mcpServers"": {
    ""filesystem"": {
      ""command"": ""npx"",
      ""args"": [""-y"", ""@modelcontextprotocol/server-filesystem"", ""C:/path/to/folder""]
    }
  }
}";
            System.IO.File.WriteAllText(configPath, exampleConfig);
        }
        
        // Open in default editor
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = configPath,
            UseShellExecute = true
        });
        
        return true;
    }

    private bool StopMcpServer(JsonElement? parameters)
    {
        var id = Guid.Parse(parameters?.GetProperty("id").GetString()!);
        _mcp.StopServer(id);
        return true;
    }

    private string CreateSuccessResponse(string? id, object? result)
    {
        var response = new BridgeResponse
        {
            Id = id,
            Result = result
        };
        return JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private string CreateErrorResponse(string? id, string message)
    {
        var response = new BridgeResponse
        {
            Id = id,
            Error = new BridgeError { Message = message }
        };
        return JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}

public class BridgeRequest
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

public class BridgeResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public BridgeError? Error { get; set; }
}

public class BridgeError
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
