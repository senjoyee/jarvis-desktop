using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ChloyeDesktop.Models;

namespace ChloyeDesktop.Services;

public class ChatService
{
    private readonly ILogger<ChatService> _logger;
    private readonly SecretsService _secrets;
    private readonly HttpClient _httpClient;

    // OpenRouter API endpoint
    private const string OpenRouterBaseUrl = "https://openrouter.ai/api/v1/chat/completions";

    public ChatService(ILogger<ChatService> logger, SecretsService secrets)
    {
        _logger = logger;
        _secrets = secrets;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    /// <summary>
    /// Get all available models from the catalog
    /// </summary>
    public static ModelDefinition[] GetAvailableModels() => ModelCatalog.AvailableModels;

    public async IAsyncEnumerable<StreamChunk> StreamCompletionAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var apiKey = _secrets.GetSecret("OpenRouter");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OpenRouter API key not configured. Please add your API key in Settings.");
        }

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, OpenRouterBaseUrl);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Headers.Add("HTTP-Referer", "https://jarvis-desktop.local");
        httpRequest.Headers.Add("X-Title", "Jarvis Desktop");

        // Build messages array for Chat Completions API
        var messages = request.Messages.Select(m => new Dictionary<string, object>
        { 
            ["role"] = m.Role == "system" ? "system" : (m.Role == "user" ? "user" : "assistant"),
            ["content"] = m.Content 
        }).ToList();

        // Build request body for OpenRouter Chat Completions API
        var bodyObj = new Dictionary<string, object>
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = true
        };

        // Handle reasoning/thinking for supported models
        // For OpenAI o-series: use reasoning_effort
        // For Gemini 2.5: uses thinking by default
        // For DeepSeek R1: uses extended thinking
        if (!string.IsNullOrEmpty(request.ReasoningEffort) && request.ReasoningEffort != "none")
        {
            var modelDef = ModelCatalog.AvailableModels.FirstOrDefault(m => m.Id == request.Model);
            if (modelDef?.SupportsReasoning == true)
            {
                // Request reasoning from OpenRouter
                bodyObj["include_reasoning"] = true;

                // OpenAI o-series models use reasoning_effort parameter
                if (request.Model.StartsWith("openai/o"))
                {
                    bodyObj["reasoning_effort"] = request.ReasoningEffort;
                }
                // For other reasoning models, they use their native thinking mechanisms
                // which are enabled by default or via provider-specific params
            }
        }
        else 
        {
             // Even if effort is none, if model supports reasoning, we might want to see if it produces any
             var modelDef = ModelCatalog.AvailableModels.FirstOrDefault(m => m.Id == request.Model);
             if (modelDef?.SupportsReasoning == true)
             {
                 bodyObj["include_reasoning"] = true;
             }
        }

        // Add tools if available
        if (request.Tools != null && request.Tools.Count > 0)
        {
            bodyObj["tools"] = request.Tools;
            _logger.LogInformation("Including {ToolCount} tools in request", request.Tools.Count);
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var jsonBody = JsonSerializer.Serialize(bodyObj, jsonOptions);
        _logger.LogInformation("Sending request to OpenRouter: model={Model}, reasoning_effort={Effort}", 
            request.Model, request.ReasoningEffort ?? "none");
        _logger.LogDebug("Request body: {Body}", jsonBody);

        httpRequest.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("OpenRouter API error: {StatusCode} - {Error}", response.StatusCode, error);
            throw new HttpRequestException($"OpenRouter API error: {response.StatusCode} - {error}");
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

            ChatCompletionStreamEvent? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ChatCompletionStreamEvent>(data);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to parse stream event: {Error}", ex.Message);
                continue;
            }

            if (parsed?.Choices == null || parsed.Choices.Count == 0)
                continue;

            var choice = parsed.Choices[0];
            var delta = choice.Delta;

            if (delta == null) continue;

            // Handle content delta
            if (!string.IsNullOrEmpty(delta.Content))
            {
                yield return new StreamChunk { Content = delta.Content };
            }

            // Handle reasoning/thinking content (some models return this separately)
            if (!string.IsNullOrEmpty(delta.Reasoning))
            {
                yield return new StreamChunk { Reasoning = delta.Reasoning };
            }

            // Handle tool calls
            if (delta.ToolCalls != null && delta.ToolCalls.Count > 0)
            {
                foreach (var toolCall in delta.ToolCalls)
                {
                    if (toolCall.Function?.Name != null)
                    {
                        // New tool call starting
                        currentToolCallId = toolCall.Id ?? Guid.NewGuid().ToString();
                        currentToolName = toolCall.Function.Name;
                        currentToolArgs.Clear();
                        _logger.LogDebug("Tool call starting: {Name}", currentToolName);
                    }

                    if (toolCall.Function?.Arguments != null)
                    {
                        currentToolArgs.Append(toolCall.Function.Arguments);
                    }
                }
            }

            // Check for finish reason
            if (choice.FinishReason == "tool_calls" && !string.IsNullOrEmpty(currentToolName))
            {
                _logger.LogInformation("Tool call complete: {ToolName}", currentToolName);
                yield return new StreamChunk
                {
                    ToolCall = new ToolCallInfo
                    {
                        Id = currentToolCallId ?? Guid.NewGuid().ToString(),
                        Name = currentToolName,
                        Arguments = currentToolArgs.ToString()
                    }
                };
                currentToolCallId = null;
                currentToolName = null;
                currentToolArgs.Clear();
            }

            if (choice.FinishReason == "stop")
            {
                // Extract usage if available
                TokenUsage? usage = null;
                if (parsed.Usage != null)
                {
                    usage = new TokenUsage
                    {
                        InputTokens = parsed.Usage.PromptTokens,
                        OutputTokens = parsed.Usage.CompletionTokens,
                        TotalTokens = parsed.Usage.TotalTokens
                    };
                }
                yield return new StreamChunk { Done = true, Usage = usage };
                yield break;
            }
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        var apiKey = _secrets.GetSecret("OpenRouter");
        if (string.IsNullOrEmpty(apiKey))
        {
            return false;
        }

        try
        {
            // Test by calling the models endpoint
            var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("HTTP-Referer", "https://jarvis-desktop.local");

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenRouter connection test failed");
            return false;
        }
    }
}

public class ChatRequest
{
    public string Model { get; set; } = "openai/gpt-5-mini";
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

// OpenRouter/OpenAI Chat Completions stream event models
internal class ChatCompletionStreamEvent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("choices")]
    public List<StreamChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; set; }
}

internal class StreamChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public StreamDelta? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal class StreamDelta
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<ToolCallDelta>? ToolCalls { get; set; }
}

internal class ToolCallDelta
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    public FunctionCallDelta? Function { get; set; }
}

internal class FunctionCallDelta
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

internal class UsageInfo
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
