using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ChloyeDesktop.Services;

public class ChatService
{
    private readonly ILogger<ChatService> _logger;
    private readonly SecretsService _secrets;
    private readonly HttpClient _httpClient;

    // Available models
    public static readonly string[] AvailableModels = { "gpt-5.2", "gpt-5-mini" };
    
    // Available reasoning effort levels
    public static readonly string[] ReasoningEffortLevels = { "none", "low", "medium", "high" };

    public ChatService(ILogger<ChatService> logger, SecretsService secrets)
    {
        _logger = logger;
        _secrets = secrets;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    public async IAsyncEnumerable<StreamChunk> StreamCompletionAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var apiKey = _secrets.GetSecret("OpenAI");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key not configured");
        }

        // Use OpenAI Responses API
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Build input from messages
        var input = request.Messages.Select(m => new 
        { 
            role = m.Role == "user" ? "user" : (m.Role == "assistant" ? "assistant" : "developer"),
            content = m.Content 
        }).ToList();

        // Build request body for Responses API
        var bodyObj = new Dictionary<string, object>
        {
            ["model"] = request.Model,
            ["input"] = input,
            ["stream"] = true
        };

        // Add reasoning effort if specified
        if (!string.IsNullOrEmpty(request.ReasoningEffort) && request.ReasoningEffort != "none")
        {
            bodyObj["reasoning"] = new { effort = request.ReasoningEffort, summary = "auto" };
        }

        // Add tools if available
        if (request.Tools != null && request.Tools.Count > 0)
        {
            bodyObj["tools"] = request.Tools;
            _logger.LogInformation("Including {ToolCount} tools in request", request.Tools.Count);
            // Debug: log the first tool structure
            var debugJson = JsonSerializer.Serialize(request.Tools[0], new JsonSerializerOptions { WriteIndented = true });
            _logger.LogDebug("First tool structure: {Tool}", debugJson);
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _logger.LogInformation("Sending request to Responses API: model={Model}, reasoning_effort={Effort}, tools={ToolCount}", 
            request.Model, request.ReasoningEffort ?? "none", request.Tools?.Count ?? 0);

        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(bodyObj, jsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("OpenAI Responses API error: {StatusCode} - {Error}", response.StatusCode, error);
            throw new HttpRequestException($"OpenAI API error: {response.StatusCode} - {error}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        // Track function call state for aggregation
        string? currentToolCallId = null;
        string? currentToolName = null;
        var currentToolArgs = new StringBuilder();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            
            if (data == "[DONE]")
            {
                yield return new StreamChunk { Done = true };
                yield break;
            }

            ResponsesStreamEvent? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ResponsesStreamEvent>(data);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to parse stream event: {Error}", ex.Message);
                continue;
            }

            _logger.LogDebug("Stream event type: {Type}", parsed?.Type ?? "(null)");

            // Handle different event types from Responses API
            // Handle reasoning/thinking tokens - match all known variants
            if (parsed?.Type != null && (
                    parsed.Type.Contains("reasoning") && parsed.Type.Contains("delta")))
            {
                using var doc = JsonDocument.Parse(data);
                string? reasoningText = null;
                
                if (doc.RootElement.TryGetProperty("delta", out var rd))
                    reasoningText = rd.GetString();
                else if (doc.RootElement.TryGetProperty("text", out var rt))
                    reasoningText = rt.GetString();
                
                if (!string.IsNullOrEmpty(reasoningText))
                {
                    yield return new StreamChunk { Reasoning = reasoningText };
                }
            }
            else if (parsed?.Type == "response.output_text.delta")
            {
                // Extract delta from raw JSON using JsonDocument
                using var doc = JsonDocument.Parse(data);
                string? deltaText = null;
                
                // Try different possible field names
                if (doc.RootElement.TryGetProperty("text_delta", out var td))
                    deltaText = td.GetString();
                else if (doc.RootElement.TryGetProperty("delta", out var d))
                    deltaText = d.GetString();
                else if (doc.RootElement.TryGetProperty("text", out var t))
                    deltaText = t.GetString();
                
                if (!string.IsNullOrEmpty(deltaText))
                {
                    yield return new StreamChunk { Content = deltaText };
                }
            }
            // Handle function call start
            else if (parsed?.Type == "response.function_call_arguments.start" || 
                     parsed?.Type == "response.output_item.added")
            {
                using var doc = JsonDocument.Parse(data);
                
                // Try to get tool name from the event
                if (doc.RootElement.TryGetProperty("item", out var item))
                {
                    if (item.TryGetProperty("call_id", out var callId))
                        currentToolCallId = callId.GetString();
                    if (item.TryGetProperty("name", out var name))
                        currentToolName = name.GetString();
                }
                else if (doc.RootElement.TryGetProperty("name", out var directName))
                {
                    currentToolName = directName.GetString();
                }
                
                if (!string.IsNullOrEmpty(currentToolName))
                {
                    _logger.LogDebug("Function call started: {ToolName}", currentToolName);
                    currentToolArgs.Clear();
                }
            }
            // Handle function call arguments streaming
            else if (parsed?.Type == "response.function_call_arguments.delta")
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("delta", out var delta))
                {
                    currentToolArgs.Append(delta.GetString());
                }
            }
            // Handle function call complete
            else if (parsed?.Type == "response.function_call_arguments.done" ||
                     parsed?.Type == "response.output_item.done")
            {
                if (!string.IsNullOrEmpty(currentToolName))
                {
                    using var doc = JsonDocument.Parse(data);
                    string? arguments = currentToolArgs.ToString();
                    
                    // Try to get arguments from the done event if not accumulated
                    if (string.IsNullOrEmpty(arguments) && doc.RootElement.TryGetProperty("item", out var item))
                    {
                        if (item.TryGetProperty("arguments", out var args))
                        {
                            arguments = args.GetString();
                        }
                    }

                    _logger.LogInformation("Function call complete: {ToolName} with args: {Args}", 
                        currentToolName, arguments?.Substring(0, Math.Min(100, arguments?.Length ?? 0)));

                    yield return new StreamChunk
                    {
                        ToolCall = new ToolCallInfo
                        {
                            Id = currentToolCallId ?? Guid.NewGuid().ToString(),
                            Name = currentToolName,
                            Arguments = arguments ?? "{}"
                        }
                    };

                    // Reset state
                    currentToolCallId = null;
                    currentToolName = null;
                    currentToolArgs.Clear();
                }
            }
            else if (parsed?.Type == "response.completed" || parsed?.Type == "response.done")
            {
                // Extract token usage from the completed response
                TokenUsage? usage = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (doc.RootElement.TryGetProperty("response", out var resp) &&
                        resp.TryGetProperty("usage", out var usageEl))
                    {
                        usage = new TokenUsage();
                        if (usageEl.TryGetProperty("input_tokens", out var inp))
                            usage.InputTokens = inp.GetInt32();
                        if (usageEl.TryGetProperty("output_tokens", out var outp))
                            usage.OutputTokens = outp.GetInt32();
                        if (usageEl.TryGetProperty("total_tokens", out var tot))
                            usage.TotalTokens = tot.GetInt32();
                        // Reasoning tokens are nested in output_tokens_details
                        if (usageEl.TryGetProperty("output_tokens_details", out var details) &&
                            details.TryGetProperty("reasoning_tokens", out var reas))
                            usage.ReasoningTokens = reas.GetInt32();
                        _logger.LogInformation("Token usage - input: {Input}, output: {Output}, reasoning: {Reasoning}",
                            usage.InputTokens, usage.OutputTokens, usage.ReasoningTokens);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Failed to parse usage from completed event: {Error}", ex.Message);
                }
                yield return new StreamChunk { Done = true, Usage = usage };
                yield break;
            }
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        var apiKey = _secrets.GetSecret("OpenAI");
        if (string.IsNullOrEmpty(apiKey))
        {
            return false;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI connection test failed");
            return false;
        }
    }
}

public class ChatRequest
{
    public string Model { get; set; } = "gpt-5.2";
    public string? ReasoningEffort { get; set; } = "medium";
    public List<ChatMessage> Messages { get; set; } = new();
    
    /// <summary>
    /// Tool definitions in OpenAI function format
    /// </summary>
    public List<object>? Tools { get; set; }
}

public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class StreamChunk
{
    public string? Content { get; set; }
    public bool Done { get; set; }
    
    /// <summary>
    /// Set when the model requests a tool call
    /// </summary>
    public ToolCallInfo? ToolCall { get; set; }
    
    /// <summary>
    /// Reasoning/thinking text delta from the model
    /// </summary>
    public string? Reasoning { get; set; }
    
    /// <summary>
    /// Token usage info from the completed response
    /// </summary>
    public TokenUsage? Usage { get; set; }
}

/// <summary>
/// Token usage information from the API response
/// </summary>
public class TokenUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ReasoningTokens { get; set; }
    public int TotalTokens { get; set; }
}

/// <summary>
/// Information about a tool call requested by the model
/// </summary>
public class ToolCallInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
}

// Responses API stream event
internal class ResponsesStreamEvent
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text_delta")]
    public string? TextDelta { get; set; }

    [JsonPropertyName("delta")]
    public string? Delta { get; set; }

    [JsonPropertyName("response")]
    public ResponsesResponse? Response { get; set; }
}

internal class ResponsesResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("output_text")]
    public string? OutputText { get; set; }
}
