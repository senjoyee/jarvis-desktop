using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ChloyeDesktop.Models;

namespace ChloyeDesktop.Services;

/// <summary>
/// Chat service that integrates with OpenRouter API.
/// OpenRouter provides a unified API to access 500+ AI models from 60+ providers.
/// API Reference: https://openrouter.ai/docs/api-reference/overview
/// </summary>
public class ChatService
{
    private readonly ILogger<ChatService> _logger;
    private readonly SecretsService _secrets;
    private readonly HttpClient _httpClient;

    // OpenRouter Chat Completions endpoint
    // Docs: https://openrouter.ai/docs/api-reference/overview
    private const string OpenRouterBaseUrl = "https://openrouter.ai/api/v1/chat/completions";

    public ChatService(ILogger<ChatService> logger, SecretsService secrets)
    {
        _logger = logger;
        _secrets = secrets;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10) // Long timeout for reasoning models
        };
    }

    /// <summary>
    /// Get all available models from the catalog
    /// </summary>
    public static ModelDefinition[] GetAvailableModels() => ModelCatalog.AvailableModels;

    /// <summary>
    /// Stream a chat completion from OpenRouter.
    /// Supports any model available on OpenRouter including:
    /// - OpenAI (GPT-5.x series)
    /// - Anthropic (Claude 4.x series)
    /// - Google (Gemini 2.5/3 series)
    /// - DeepSeek, xAI, Meta, Mistral, Qwen, etc.
    /// </summary>
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
        
        // Required: Bearer token authentication
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        
        // Optional but recommended: Identify your app for rankings on openrouter.ai
        httpRequest.Headers.Add("HTTP-Referer", "https://jarvis-desktop.local");
        httpRequest.Headers.Add("X-Title", "Jarvis Desktop");

        // Build messages array following OpenAI Chat API format
        // OpenRouter normalizes this across all providers
        var messages = request.Messages.Select(m => new Dictionary<string, object>
        { 
            ["role"] = m.Role,  // "system", "user", "assistant", or "tool"
            ["content"] = m.Content 
        }).ToList();

        // Build request body per OpenRouter API spec
        // Docs: https://openrouter.ai/docs/api-reference/overview
        var bodyObj = new Dictionary<string, object>
        {
            // Required: Model ID in format "provider/model-name"
            // e.g., "openai/gpt-5-mini", "anthropic/claude-opus-4.6", "google/gemini-2.5-flash"
            ["model"] = request.Model,
            
            // Required: Messages array
            ["messages"] = messages,
            
            // Enable streaming for real-time response
            ["stream"] = true
        };

        // Add tools if available (OpenRouter supports tool calling for compatible models)
        // Docs: https://openrouter.ai/docs/guides/features/tool-calling
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
        _logger.LogInformation("OpenRouter request: model={Model}", request.Model);
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

        // Track function call state for aggregation (tool calls come in chunks)
        string? currentToolCallId = null;
        string? currentToolName = null;
        var currentToolArgs = new StringBuilder();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            
            // Skip empty lines
            if (string.IsNullOrEmpty(line)) continue;
            
            // Skip SSE comments (OpenRouter sends these to prevent timeouts)
            // Per SSE spec, lines starting with ":" are comments
            if (line.StartsWith(":")) continue;
            
            // SSE data lines start with "data: "
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..]; // Remove "data: " prefix
            
            // "[DONE]" signals end of stream
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
                _logger.LogDebug("Failed to parse stream event: {Error}, Data: {Data}", ex.Message, data);
                continue;
            }

            if (parsed?.Choices == null || parsed.Choices.Count == 0)
            {
                // Final chunk with just usage stats (no choices)
                if (parsed?.Usage != null)
                {
                    yield return new StreamChunk
                    {
                        Done = true,
                        Usage = new TokenUsage
                        {
                            InputTokens = parsed.Usage.PromptTokens,
                            OutputTokens = parsed.Usage.CompletionTokens,
                            TotalTokens = parsed.Usage.TotalTokens,
                            ReasoningTokens = parsed.Usage.CompletionTokensDetails?.ReasoningTokens ?? 0,
                            Cost = parsed.Usage.Cost ?? 0
                        }
                    };
                }
                continue;
            }

            var choice = parsed.Choices[0];
            var delta = choice.Delta;

            if (delta == null) continue;

            // Handle content delta (main response text)
            if (!string.IsNullOrEmpty(delta.Content))
            {
                yield return new StreamChunk { Content = delta.Content };
            }

            // Handle reasoning/thinking content
            // Some models (o1, o3, Gemini thinking, etc.) return reasoning separately
            if (!string.IsNullOrEmpty(delta.Reasoning))
            {
                yield return new StreamChunk { Reasoning = delta.Reasoning };
            }

            // Handle tool calls (streamed in chunks)
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
            // OpenRouter normalizes to: tool_calls, stop, length, content_filter, error
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
                // Extract usage if available in this chunk
                TokenUsage? usage = null;
                if (parsed.Usage != null)
                {
                    usage = new TokenUsage
                    {
                        InputTokens = parsed.Usage.PromptTokens,
                        OutputTokens = parsed.Usage.CompletionTokens,
                        TotalTokens = parsed.Usage.TotalTokens,
                        ReasoningTokens = parsed.Usage.CompletionTokensDetails?.ReasoningTokens ?? 0,
                        Cost = parsed.Usage.Cost ?? 0
                    };
                }
                yield return new StreamChunk { Done = true, Usage = usage };
                yield break;
            }
        }
    }

    /// <summary>
    /// Test connection to OpenRouter by calling the models endpoint
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        var apiKey = _secrets.GetSecret("OpenRouter");
        if (string.IsNullOrEmpty(apiKey))
        {
            return false;
        }

        try
        {
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

/// <summary>
/// Request for chat completion
/// </summary>
public class ChatRequest
{
    /// <summary>
    /// Model ID in OpenRouter format: "provider/model-name"
    /// Examples: "openai/gpt-5-mini", "anthropic/claude-opus-4.6", "google/gemini-2.5-flash"
    /// </summary>
    public string Model { get; set; } = "openai/gpt-5-mini";
    
    /// <summary>
    /// Conversation messages
    /// </summary>
    public List<ChatMessage> Messages { get; set; } = new();
    
    /// <summary>
    /// Tool definitions in OpenAI function format
    /// OpenRouter transforms these for non-OpenAI providers
    /// </summary>
    public List<object>? Tools { get; set; }
}

/// <summary>
/// A message in the conversation
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// Role: "system", "user", "assistant", or "tool"
    /// </summary>
    public string Role { get; set; } = string.Empty;
    
    /// <summary>
    /// Message content
    /// </summary>
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// A chunk of streamed response data
/// </summary>
public class StreamChunk
{
    /// <summary>
    /// Text content delta
    /// </summary>
    public string? Content { get; set; }
    
    /// <summary>
    /// True when stream is complete
    /// </summary>
    public bool Done { get; set; }
    
    /// <summary>
    /// Set when the model requests a tool call
    /// </summary>
    public ToolCallInfo? ToolCall { get; set; }
    
    /// <summary>
    /// Reasoning/thinking text delta (for reasoning models)
    /// </summary>
    public string? Reasoning { get; set; }
    
    /// <summary>
    /// Token usage info (included in final chunk)
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
    
    /// <summary>
    /// Cost in USD (from OpenRouter)
    /// </summary>
    public decimal Cost { get; set; }
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

// ============================================================
// OpenRouter SSE Stream Event Models
// Based on OpenAI Chat API format (OpenRouter normalizes all providers to this)
// Docs: https://openrouter.ai/docs/api-reference/overview
// ============================================================

internal class ChatCompletionStreamEvent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("choices")]
    public List<StreamChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; set; }
    
    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

internal class StreamChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public StreamDelta? Delta { get; set; }

    /// <summary>
    /// Normalized finish reason: tool_calls, stop, length, content_filter, error
    /// </summary>
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
    
    /// <summary>
    /// Raw finish reason from the provider
    /// </summary>
    [JsonPropertyName("native_finish_reason")]
    public string? NativeFinishReason { get; set; }
}

internal class StreamDelta
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// Reasoning/thinking content (for reasoning-capable models)
    /// </summary>
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
    
    /// <summary>
    /// Cost in credits (from OpenRouter)
    /// </summary>
    [JsonPropertyName("cost")]
    public decimal? Cost { get; set; }
    
    /// <summary>
    /// Detailed breakdown of completion tokens
    /// </summary>
    [JsonPropertyName("completion_tokens_details")]
    public CompletionTokensDetails? CompletionTokensDetails { get; set; }
}

internal class CompletionTokensDetails
{
    /// <summary>
    /// Tokens used for reasoning/thinking (o1, o3, Gemini thinking, etc.)
    /// </summary>
    [JsonPropertyName("reasoning_tokens")]
    public int ReasoningTokens { get; set; }
}
