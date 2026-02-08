namespace ChloyeDesktop.Models;

/// <summary>
/// Represents an AI model available through OpenRouter
/// </summary>
public class ModelDefinition
{
    /// <summary>
    /// The model ID used in API calls (e.g., "openai/gpt-4o")
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable name for display (e.g., "GPT-4o")
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Provider name for grouping (e.g., "OpenAI", "Anthropic")
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>
    /// Whether this model supports reasoning/thinking mode
    /// </summary>
    public bool SupportsReasoning { get; init; }

    /// <summary>
    /// Optional description of the model
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Context window size in tokens
    /// </summary>
    public int ContextLength { get; init; }
}

/// <summary>
/// Static catalog of available models through OpenRouter
/// </summary>
public static class ModelCatalog
{
    public static readonly ModelDefinition[] AvailableModels = new[]
    {
        // OpenAI Models
        new ModelDefinition
        {
            Id = "openai/gpt-4o",
            Name = "GPT-4o",
            Provider = "OpenAI",
            SupportsReasoning = false,
            Description = "Most capable GPT-4 model, multimodal",
            ContextLength = 128000
        },
        new ModelDefinition
        {
            Id = "openai/gpt-4o-mini",
            Name = "GPT-4o Mini",
            Provider = "OpenAI",
            SupportsReasoning = false,
            Description = "Affordable and intelligent small model",
            ContextLength = 128000
        },
        new ModelDefinition
        {
            Id = "openai/o1",
            Name = "o1",
            Provider = "OpenAI",
            SupportsReasoning = true,
            Description = "Advanced reasoning model",
            ContextLength = 200000
        },
        new ModelDefinition
        {
            Id = "openai/o1-mini",
            Name = "o1 Mini",
            Provider = "OpenAI",
            SupportsReasoning = true,
            Description = "Fast reasoning model",
            ContextLength = 128000
        },
        new ModelDefinition
        {
            Id = "openai/o3-mini",
            Name = "o3 Mini",
            Provider = "OpenAI",
            SupportsReasoning = true,
            Description = "Latest small reasoning model",
            ContextLength = 200000
        },

        // Anthropic Models
        new ModelDefinition
        {
            Id = "anthropic/claude-sonnet-4",
            Name = "Claude Sonnet 4",
            Provider = "Anthropic",
            SupportsReasoning = false,
            Description = "Latest Claude Sonnet model",
            ContextLength = 200000
        },
        new ModelDefinition
        {
            Id = "anthropic/claude-3.5-sonnet",
            Name = "Claude 3.5 Sonnet",
            Provider = "Anthropic",
            SupportsReasoning = false,
            Description = "Best balance of speed and intelligence",
            ContextLength = 200000
        },
        new ModelDefinition
        {
            Id = "anthropic/claude-3.5-haiku",
            Name = "Claude 3.5 Haiku",
            Provider = "Anthropic",
            SupportsReasoning = false,
            Description = "Fast and affordable",
            ContextLength = 200000
        },
        new ModelDefinition
        {
            Id = "anthropic/claude-3-opus",
            Name = "Claude 3 Opus",
            Provider = "Anthropic",
            SupportsReasoning = false,
            Description = "Most capable Claude model",
            ContextLength = 200000
        },

        // Google Models
        new ModelDefinition
        {
            Id = "google/gemini-2.5-pro-preview",
            Name = "Gemini 2.5 Pro",
            Provider = "Google",
            SupportsReasoning = true,
            Description = "Most capable Gemini model with thinking",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "google/gemini-2.5-flash-preview",
            Name = "Gemini 2.5 Flash",
            Provider = "Google",
            SupportsReasoning = true,
            Description = "Fast Gemini with thinking support",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "google/gemini-2.0-flash-001",
            Name = "Gemini 2.0 Flash",
            Provider = "Google",
            SupportsReasoning = false,
            Description = "Latest fast Gemini model",
            ContextLength = 1000000
        },

        // Moonshot (Kimi) Models
        new ModelDefinition
        {
            Id = "moonshotai/kimi-k2",
            Name = "Kimi K2",
            Provider = "Moonshot",
            SupportsReasoning = false,
            Description = "Advanced Moonshot model with strong agentic capabilities",
            ContextLength = 128000
        },
        new ModelDefinition
        {
            Id = "moonshotai/kimi-vl-a3b-thinking",
            Name = "Kimi VL Thinking",
            Provider = "Moonshot",
            SupportsReasoning = true,
            Description = "Vision-language model with thinking",
            ContextLength = 128000
        },

        // DeepSeek Models
        new ModelDefinition
        {
            Id = "deepseek/deepseek-r1",
            Name = "DeepSeek R1",
            Provider = "DeepSeek",
            SupportsReasoning = true,
            Description = "Advanced reasoning model",
            ContextLength = 64000
        },
        new ModelDefinition
        {
            Id = "deepseek/deepseek-chat",
            Name = "DeepSeek Chat",
            Provider = "DeepSeek",
            SupportsReasoning = false,
            Description = "General purpose chat model",
            ContextLength = 64000
        },

        // Meta Models
        new ModelDefinition
        {
            Id = "meta-llama/llama-3.3-70b-instruct",
            Name = "Llama 3.3 70B",
            Provider = "Meta",
            SupportsReasoning = false,
            Description = "Large open-weight model",
            ContextLength = 131072
        },
        new ModelDefinition
        {
            Id = "meta-llama/llama-4-maverick",
            Name = "Llama 4 Maverick",
            Provider = "Meta",
            SupportsReasoning = false,
            Description = "Latest Llama 4 model",
            ContextLength = 1000000
        },

        // Mistral Models
        new ModelDefinition
        {
            Id = "mistralai/mistral-large-2411",
            Name = "Mistral Large",
            Provider = "Mistral",
            SupportsReasoning = false,
            Description = "Flagship Mistral model",
            ContextLength = 128000
        },
        new ModelDefinition
        {
            Id = "mistralai/codestral-2501",
            Name = "Codestral",
            Provider = "Mistral",
            SupportsReasoning = false,
            Description = "Specialized for code generation",
            ContextLength = 256000
        },

        // xAI Models
        new ModelDefinition
        {
            Id = "x-ai/grok-2-1212",
            Name = "Grok 2",
            Provider = "xAI",
            SupportsReasoning = false,
            Description = "xAI's flagship model",
            ContextLength = 131072
        },

        // Qwen Models
        new ModelDefinition
        {
            Id = "qwen/qwen-2.5-72b-instruct",
            Name = "Qwen 2.5 72B",
            Provider = "Qwen",
            SupportsReasoning = false,
            Description = "Alibaba's large language model",
            ContextLength = 131072
        },
        new ModelDefinition
        {
            Id = "qwen/qwq-32b",
            Name = "QwQ 32B",
            Provider = "Qwen",
            SupportsReasoning = true,
            Description = "Qwen reasoning model",
            ContextLength = 131072
        }
    };

    /// <summary>
    /// Get all unique provider names
    /// </summary>
    public static string[] GetProviders()
    {
        return AvailableModels
            .Select(m => m.Provider)
            .Distinct()
            .OrderBy(p => p)
            .ToArray();
    }

    /// <summary>
    /// Get models grouped by provider
    /// </summary>
    public static Dictionary<string, ModelDefinition[]> GetModelsByProvider()
    {
        return AvailableModels
            .GroupBy(m => m.Provider)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }
}
