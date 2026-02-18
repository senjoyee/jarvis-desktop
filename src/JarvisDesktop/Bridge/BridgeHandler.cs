using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using JarvisDesktop.Models;
using JarvisDesktop.Services;

namespace JarvisDesktop.Bridge;

public class BridgeHandler
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BridgeHandler> _logger;
    private readonly ConversationService _conversations;
    private readonly ChatService _chat;
    private readonly SecretsService _secrets;
    private readonly McpManager _mcp;
    private readonly SkillService _skills;
    private readonly CodeExecutionService _codeExec;
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
        _secrets = services.GetRequiredService<SecretsService>();
        _mcp = services.GetRequiredService<McpManager>();
        _skills = services.GetRequiredService<SkillService>();
        _codeExec = services.GetRequiredService<CodeExecutionService>();
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
            "conversations.pin" => _conversations.TogglePinConversation(
                Guid.Parse(parameters?.GetProperty("id").GetString()!),
                parameters?.GetProperty("isPinned").GetBoolean() ?? false),

            // Messages
            "messages.list" => _conversations.GetMessages(
                Guid.Parse(parameters?.GetProperty("conversationId").GetString()!)),
            "messages.send" => await HandleSendMessage(parameters),
            "messages.stopStream" => StopStream(),

            // Settings (OpenRouter)
            "settings.hasApiKey" => _secrets.HasSecret("OpenRouter"),
            "settings.setApiKey" => SetApiKey(parameters?.GetProperty("key").GetString()!),
            "settings.clearApiKey" => _secrets.DeleteSecret("OpenRouter"),
            "settings.testOpenRouter" => await _chat.TestConnectionAsync(),

            // Models
            "models.list" => GetAvailableModels(),

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

            // Code Mode
            "codeMode.checkNode" => await _codeExec.IsNodeAvailableAsync(),
            "codeMode.searchTools" => await _codeExec.SearchToolsAsync(
                parameters?.GetProperty("query").GetString()!,
                parameters?.TryGetProperty("detailLevel", out var dl) == true ? dl.GetString()! : "full"),

            _ => throw new NotSupportedException($"Unknown method: {method}")
        };
    }

    private async Task<object> HandleSendMessage(JsonElement? parameters)
    {
        var conversationId = Guid.Parse(parameters?.GetProperty("conversationId").GetString()!);
        var content = parameters?.GetProperty("content").GetString()!;
        var model = parameters?.GetProperty("model").GetString() ?? "openai/gpt-5-mini";
        
        // Check if Code Mode is enabled
        var codeMode = parameters?.TryGetProperty("codeMode", out var cm) == true && cm.GetBoolean();

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

        List<object> openAiTools;
        string systemPrompt;
        List<McpToolWithServer> validTools;

        if (codeMode)
        {
            // === CODE MODE ===
            // Instead of loading all individual tool definitions, provide:
            // 1. execute_code — runs TypeScript in a sandbox with MCP tool access
            // 2. search_tools — lets the LLM discover available tools progressively
            _logger.LogInformation("Code Mode enabled — generating tool wrappers");
            
            // Generate the tool wrapper files
            var toolTree = await _codeExec.GetToolTreeSummaryAsync();
            
            // Build Code Mode tools (just 2 instead of potentially hundreds)
            openAiTools = BuildCodeModeTools();
            validTools = new List<McpToolWithServer>(); // Not used in code mode prompt

            // Build Code Mode system prompt
            var connectedServers = _mcp.ListServers()
                .Where(s => _mcp.GetStatus(s.Id) == McpConnectionStatus.Connected)
                .Select(s => s.Name)
                .ToList();
            systemPrompt = BuildCodeModeSystemPrompt(connectedServers, toolTree);
            
            _logger.LogInformation("Code Mode: providing 2 tools (execute_code, search_tools) instead of individual tool definitions");
        }
        else
        {
            // === DIRECT MODE (existing behavior) ===
            var mcpTools = await _mcp.GetAllToolsAsync();
            validTools = mcpTools.Where(t => !string.IsNullOrEmpty(t.Tool.Name)).ToList();
            openAiTools = validTools.Select(t => ConvertToOpenAiFunction(t.Tool)).ToList();
            _logger.LogInformation("Direct Mode: loaded {ToolCount} valid tools from {ServerCount} MCP servers (filtered {FilteredCount} invalid)", 
                openAiTools.Count, validTools.Select(t => t.ServerId).Distinct().Count(), mcpTools.Count - validTools.Count);

            var connectedServers = _mcp.ListServers()
                .Where(s => _mcp.GetStatus(s.Id) == McpConnectionStatus.Connected)
                .Select(s => s.Name)
                .ToList();
            systemPrompt = BuildSystemPrompt(connectedServers, validTools);
        }
        
        // Insert system message at the beginning
        var messagesWithSystem = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = systemPrompt }
        };
        messagesWithSystem.AddRange(chatMessages);

        var request = new ChatRequest
        {
            Model = model,
            Messages = messagesWithSystem,
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
            const int maxToolCalls = 30; // Prevent infinite loops
            var toolCallCount = 0;

            // Accumulate token usage across tool-call loop iterations
            int usageInput = 0, usageOutput = 0, usageReasoning = 0, usageTotal = 0;
            decimal usageCost = 0;

            while (toolCallCount < maxToolCalls)
            {
                ToolCallInfo? pendingToolCall = null;

                await foreach (var chunk in _chat.StreamCompletionAsync(request, _streamCts.Token))
                {
                    if (chunk.Done)
                    {
                        // Accumulate usage from this iteration
                        if (chunk.Usage != null)
                        {
                            usageInput += chunk.Usage.InputTokens;
                            usageOutput += chunk.Usage.OutputTokens;
                            usageReasoning += chunk.Usage.ReasoningTokens;
                            usageTotal += chunk.Usage.TotalTokens;
                            usageCost += chunk.Usage.Cost;
                        }
                        break;
                    }
                    
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
                    
                    // Send reasoning/thinking tokens to UI
                    if (!string.IsNullOrEmpty(chunk.Reasoning))
                    {
                        SendStreamEvent("stream.reasoning", new
                        {
                            messageId = assistantMessage.Id.ToString(),
                            delta = chunk.Reasoning
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

                    // Notify UI about tool call with details
                    SendStreamEvent("stream.toolCallStart", new
                    {
                        messageId = assistantMessage.Id.ToString(),
                        toolName = pendingToolCall.Name,
                        arguments = pendingToolCall.Arguments
                    });

                    // Also send inline text for the stored message content
                    var toolCallText = $"\n\n🔧 *Calling tool: **{pendingToolCall.Name}***\n";
                    fullContent.Append(toolCallText);
                    SendStreamEvent("stream.delta", new
                    {
                        messageId = assistantMessage.Id.ToString(),
                        delta = toolCallText
                    });

                    // Execute the tool via MCP (or Code Mode sandbox)
                    string toolResultText;
                    bool toolSuccess = true;
                    try
                    {
                        if (codeMode && pendingToolCall.Name == "execute_code")
                        {
                            // === CODE MODE: Execute TypeScript in sandbox ===
                            var argsJson = JsonDocument.Parse(pendingToolCall.Arguments).RootElement;
                            var code = argsJson.GetProperty("code").GetString()!;
                            
                            _logger.LogInformation("Code Mode: executing TypeScript code ({Length} chars)", code.Length);
                            var codeResult = await _codeExec.ExecuteCodeAsync(code, _streamCts!.Token);
                            toolResultText = codeResult.ToResultString();
                            toolSuccess = codeResult.Success;
                            
                            if (codeResult.TimedOut)
                            {
                                _logger.LogWarning("Code execution timed out");
                            }
                        }
                        else if (codeMode && pendingToolCall.Name == "search_tools")
                        {
                            // === CODE MODE: Progressive tool discovery ===
                            var argsJson = JsonDocument.Parse(pendingToolCall.Arguments).RootElement;
                            var query = argsJson.GetProperty("query").GetString()!;
                            var detailLevel = argsJson.TryGetProperty("detail_level", out var dlProp)
                                ? dlProp.GetString()! : "full";
                            
                            toolResultText = await _codeExec.SearchToolsAsync(query, detailLevel);
                        }
                        else
                        {
                            // === DIRECT MODE: Call MCP tool directly ===
                            var argsJson = JsonDocument.Parse(pendingToolCall.Arguments).RootElement;
                            var result = await _mcp.CallToolByNameAsync(pendingToolCall.Name, argsJson);
                            
                            // Extract text from result
                            toolResultText = ExtractToolResultText(result);
                        }
                        _logger.LogDebug("Tool result: {Result}", toolResultText.Substring(0, Math.Min(200, toolResultText.Length)));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Tool call failed: {ToolName}", pendingToolCall.Name);
                        toolResultText = $"Error: {ex.Message}";
                        toolSuccess = false;
                    }

                    // Send detailed tool result to UI
                    SendStreamEvent("stream.toolCallResult", new
                    {
                        messageId = assistantMessage.Id.ToString(),
                        toolName = pendingToolCall.Name,
                        result = toolResultText.Length > 2000 
                            ? toolResultText.Substring(0, 2000) + "... (truncated)" 
                            : toolResultText,
                        success = toolSuccess
                    });

                    // Also send inline text for the stored message content
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
            
            // Signal stream complete with token usage
            SendStreamEvent("stream.done", new
            {
                messageId = assistantMessage.Id.ToString(),
                usage = usageTotal > 0 ? new
                {
                    inputTokens = usageInput,
                    outputTokens = usageOutput,
                    reasoningTokens = usageReasoning,
                    totalTokens = usageTotal,
                    cost = usageCost
                } : null
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during streaming");
            var errorText = $"⚠️ Error: {ex.Message}";
            fullContent.Append(errorText);
            _conversations.UpdateMessageContent(assistantMessage.Id, fullContent.ToString());
            
            // Send error to UI
            SendStreamEvent("stream.delta", new
            {
                messageId = assistantMessage.Id.ToString(),
                delta = errorText
            });
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
    /// Converts an MCP tool definition to OpenAI Chat Completions API function format
    /// OpenRouter uses this format: https://openrouter.ai/docs/api-reference/parameters
    /// </summary>
    private object ConvertToOpenAiFunction(McpTool tool)
    {
        // OpenAI Chat Completions API format requires:
        // { type: "function", function: { name, description, parameters } }
        // where parameters must have "additionalProperties": false for strict mode
        
        object parameters;
        
        if (tool.InputSchema.HasValue)
        {
            // Parse the existing schema and ensure additionalProperties is set to false
            var schemaJson = tool.InputSchema.Value.GetRawText();
            using var doc = JsonDocument.Parse(schemaJson);
            var schemaDict = new Dictionary<string, object>();
            
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    schemaDict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText())!;
                }
                else if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    schemaDict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText())!;
                }
                else if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    schemaDict[prop.Name] = prop.Value.GetString()!;
                }
                else if (prop.Value.ValueKind == JsonValueKind.Number)
                {
                    schemaDict[prop.Name] = prop.Value.GetRawText();
                }
                else if (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False)
                {
                    schemaDict[prop.Name] = prop.Value.GetBoolean();
                }
            }
            
            // Ensure additionalProperties is false (required by OpenAI)
            schemaDict["additionalProperties"] = false;
            
            // Ensure type is "object"
            if (!schemaDict.ContainsKey("type"))
            {
                schemaDict["type"] = "object";
            }
            
            parameters = schemaDict;
        }
        else
        {
            parameters = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>(),
                ["additionalProperties"] = false
            };
        }
        
        // Return in OpenAI Chat Completions format
        return new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description ?? "",
                parameters = parameters
            }
        };
    }

    /// <summary>
    /// Builds a system prompt with MCP context
    /// </summary>
    private string BuildSystemPrompt(List<string> connectedServers, List<McpToolWithServer> tools)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are Jarvis, a helpful AI assistant running in a desktop application.");
        sb.AppendLine();
        sb.AppendLine("## MCP (Model Context Protocol) Integration");
        sb.AppendLine("This application supports MCP servers which provide you with tools to interact with external systems.");
        sb.AppendLine();
        
        if (connectedServers.Count > 0)
        {
            sb.AppendLine($"**Connected MCP Servers ({connectedServers.Count}):**");
            foreach (var server in connectedServers)
            {
                sb.AppendLine($"- {server}");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("**No MCP servers are currently connected.**");
            sb.AppendLine();
        }
        
        if (tools.Count > 0)
        {
            sb.AppendLine($"**Available Tools ({tools.Count}):**");
            foreach (var tool in tools.Take(20)) // Limit to avoid token bloat
            {
                sb.AppendLine($"- `{tool.Tool.Name}` ({tool.ServerName}): {tool.Tool.Description ?? "No description"}");
            }
            if (tools.Count > 20)
            {
                sb.AppendLine($"- ... and {tools.Count - 20} more tools");
            }
            sb.AppendLine();
            sb.AppendLine("Use these tools when appropriate to help the user. When asked about MCP servers or available tools, refer to the list above.");
        }
        else
        {
            sb.AppendLine("No tools are currently available. The user may need to connect MCP servers first.");
        }

        // Inject Skills
        var skills = _skills.GetSkills();
        if (skills.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Agent Skills");
            sb.AppendLine("You are equipped with the following specialized skills. These allow you to perform complex tasks by following specific instructions.");
            sb.AppendLine("To use a skill, you MUST first read its instruction file using the `view_file` tool on the provided path. Do NOT guess the instructions.");
            sb.AppendLine();
            sb.AppendLine("**Available Skills:**");
            
            foreach (var skill in skills)
            {
                sb.AppendLine($"- **{skill.Name}**: {skill.Description}");
                sb.AppendLine($"  Path: `{skill.Path}`");
            }
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Builds the two OpenAI function definitions for Code Mode:
    /// 1. execute_code — runs TypeScript code in a sandboxed environment
    /// 2. search_tools — finds available tools by keyword
    /// </summary>
    private List<object> BuildCodeModeTools()
    {
        return new List<object>
        {
            new
            {
                type = "function",
                function = new
                {
                    name = "execute_code",
                    description = "Execute TypeScript code in a sandboxed environment with access to MCP tools. " +
                        "Import tools from the './servers/' directory (e.g., import { toolName } from './servers/server-name'). " +
                        "Use console.log() to output results that you want to see. " +
                        "Tool wrappers return MCP results — use extractText() helper to get text content. " +
                        "All intermediate data stays in the sandbox and doesn't consume context tokens.",
                    parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["code"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "TypeScript code to execute. Can import from './servers/{server-name}' directories. " +
                                    "Use 'import { extractText } from \"./__mcp_bridge\"' to extract text from MCP results."
                            }
                        },
                        ["required"] = new[] { "code" },
                        ["additionalProperties"] = false
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "search_tools",
                    description = "Search available MCP tools by keyword. Use this to discover what tools are available " +
                        "before writing code. Returns tool names, descriptions, and import instructions.",
                    parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["query"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "Search keyword to find relevant tools (matches against tool names, descriptions, and server names)"
                            },
                            ["detail_level"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "name", "description", "full" },
                                ["description"] = "Level of detail: 'name' for just names, 'description' for name+description, 'full' for complete info with schemas"
                            }
                        },
                        ["required"] = new[] { "query" },
                        ["additionalProperties"] = false
                    }
                }
            }
        };
    }

    /// <summary>
    /// Builds the system prompt for Code Mode, instructing the LLM to write code
    /// that imports and calls MCP tools rather than calling them directly.
    /// </summary>
    private string BuildCodeModeSystemPrompt(List<string> connectedServers, string toolTree)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are Jarvis, a helpful AI assistant running in a desktop application.");
        sb.AppendLine();
        sb.AppendLine("## Code Execution Mode (Active)");
        sb.AppendLine("You are in **Code Mode**. Instead of calling tools directly, you write TypeScript code that imports and calls them.");
        sb.AppendLine("This is more efficient because intermediate data stays in the execution environment and doesn't consume context tokens.");
        sb.AppendLine();
        sb.AppendLine("### How It Works");
        sb.AppendLine("1. Use the `search_tools` function to discover available tools by keyword");
        sb.AppendLine("2. Use the `execute_code` function to run TypeScript code that imports and calls tools");
        sb.AppendLine("3. Use `console.log()` to output only the data you need — this is what you'll see back");
        sb.AppendLine("4. All intermediate data processing (filtering, transformation, joining) happens in-code");
        sb.AppendLine();
        sb.AppendLine("### Available MCP Servers");
        
        if (connectedServers.Count > 0)
        {
            sb.AppendLine($"**Connected servers ({connectedServers.Count}):**");
            foreach (var server in connectedServers)
            {
                sb.AppendLine($"- {server}");
            }
        }
        else
        {
            sb.AppendLine("No MCP servers currently connected.");
        }
        
        sb.AppendLine();
        sb.AppendLine("### Tool Filesystem");
        sb.AppendLine("Tools are organized as importable TypeScript modules:");
        sb.AppendLine("```");
        sb.AppendLine("servers/");
        sb.Append(toolTree);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### Code Examples");
        sb.AppendLine("```typescript");
        sb.AppendLine("// Import tools from a server");
        sb.AppendLine("import { toolName } from './servers/server-name';");
        sb.AppendLine("import { extractText } from './__mcp_bridge';");
        sb.AppendLine();
        sb.AppendLine("// Call a tool and process results");
        sb.AppendLine("const result = await toolName({ param: 'value' });");
        sb.AppendLine("const text = extractText(result);");
        sb.AppendLine("console.log(text);");
        sb.AppendLine();
        sb.AppendLine("// Process data in-code to avoid bloating context");
        sb.AppendLine("const items = JSON.parse(text);");
        sb.AppendLine("const filtered = items.filter(i => i.status === 'active');");
        sb.AppendLine("console.log(`Found ${filtered.length} active items`);");
        sb.AppendLine("console.log(JSON.stringify(filtered.slice(0, 5), null, 2));");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### Important Guidelines");
        sb.AppendLine("- Always use `search_tools` first if you're unsure which tools are available");
        sb.AppendLine("- Use `console.log()` to output results — only logged output is returned to you");
        sb.AppendLine("- Filter and transform data in code to keep context efficient");
        sb.AppendLine("- You can chain multiple tool calls in a single `execute_code` invocation");
        sb.AppendLine("- Handle errors with try/catch blocks in your code");
        sb.AppendLine("- For simple questions that don't require tools, respond normally without code");

        // Inject Skills
        var skills = _skills.GetSkills();
        if (skills.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Agent Skills");
            sb.AppendLine("You are equipped with the following specialized skills:");
            
            foreach (var skill in skills)
            {
                sb.AppendLine($"- **{skill.Name}**: {skill.Description}");
                sb.AppendLine($"  Path: `{skill.Path}`");
            }
            sb.AppendLine();
        }
        
        return sb.ToString();
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
        _secrets.SetSecret("OpenRouter", key);
        return true;
    }

    private object GetAvailableModels()
    {
        return ModelCatalog.AvailableModels.Select(m => new
        {
            id = m.Id,
            name = m.Name,
            provider = m.Provider,
            supportsReasoning = m.SupportsReasoning,
            description = m.Description,
            contextLength = m.ContextLength
        }).ToList();
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
