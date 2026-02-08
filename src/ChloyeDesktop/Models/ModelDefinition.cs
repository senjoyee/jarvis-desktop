namespace ChloyeDesktop.Models;

/// <summary>
/// Represents an AI model available through OpenRouter
/// </summary>
public class ModelDefinition
{
    /// <summary>
    /// The model ID used in API calls (e.g., "openai/gpt-5.2")
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable name for display (e.g., "GPT-5.2")
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
/// Static catalog of available models through OpenRouter (Updated with Verified IDs)
/// </summary>
public static class ModelCatalog
{
    public static readonly ModelDefinition[] AvailableModels = new[]
    {
        // ============ OpenAI Models ============
        new ModelDefinition
        {
            Id = "openai/gpt-5.2",
            Name = "GPT-5.2",
            Provider = "OpenAI",
            SupportsReasoning = false,
            Description = "Latest flagship model",
            ContextLength = 400000
        },
        new ModelDefinition
        {
            Id = "openai/gpt-5.2-pro",
            Name = "GPT-5.2 Pro",
            Provider = "OpenAI",
            SupportsReasoning = true,
            Description = "Most capable GPT-5.2 model",
            ContextLength = 400000
        },
        new ModelDefinition
        {
            Id = "openai/gpt-5.2-chat",
            Name = "GPT-5.2 Chat",
            Provider = "OpenAI",
            SupportsReasoning = false,
            Description = "Optimized for chat interactions",
            ContextLength = 400000
        },
        new ModelDefinition
        {
            Id = "openai/gpt-5.1-codex",
            Name = "GPT-5.1 Codex",
            Provider = "OpenAI",
            SupportsReasoning = false,
            Description = "Specialized for code generation",
            ContextLength = 256000
        },
        new ModelDefinition
        {
            Id = "openai/gpt-5-mini",
            Name = "GPT-5 Mini",
            Provider = "OpenAI",
            SupportsReasoning = false,
            Description = "Fast and cost-effective",
            ContextLength = 200000
        },
        new ModelDefinition
        {
            Id = "openai/o3-pro",
            Name = "o3 Pro",
            Provider = "OpenAI",
            SupportsReasoning = true,
            Description = "Advanced reasoning model",
            ContextLength = 200000
        },
        new ModelDefinition
        {
            Id = "openai/o4-mini",
            Name = "o4 Mini",
            Provider = "OpenAI",
            SupportsReasoning = true,
            Description = "Fast reasoning model",
            ContextLength = 128000
        },

        // ============ Anthropic Models ============
        new ModelDefinition
        {
            Id = "anthropic/claude-opus-4.6",
            Name = "Claude Opus 4.6",
            Provider = "Anthropic",
            SupportsReasoning = true,
            Description = "Latest flagship (Feb 2026)",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "anthropic/claude-sonnet-4.5",
            Name = "Claude Sonnet 4.5",
            Provider = "Anthropic",
            SupportsReasoning = true,
            Description = "Optimized for agentic workflows",
            ContextLength = 200000
        },
        new ModelDefinition
        {
            Id = "anthropic/claude-3.7-sonnet",
            Name = "Claude 3.7 Sonnet",
            Provider = "Anthropic",
            SupportsReasoning = false,
            Description = "Balanced performance (Series 3)",
            ContextLength = 200000
        },
        new ModelDefinition
        {
            Id = "anthropic/claude-sonnet-4",
            Name = "Claude Sonnet 4",
            Provider = "Anthropic",
            SupportsReasoning = false,
            Description = "Balanced performance (Series 4)",
            ContextLength = 200000
        },
        new ModelDefinition
        {
            Id = "anthropic/claude-haiku-4.5",
            Name = "Claude Haiku 4.5",
            Provider = "Anthropic",
            SupportsReasoning = false,
            Description = "Fast and affordable",
            ContextLength = 200000
        },

        // ============ xAI (Grok) Models ============
        new ModelDefinition
        {
            Id = "x-ai/grok-4",
            Name = "Grok 4",
            Provider = "xAI",
            SupportsReasoning = true,
            Description = "Next-gen flagship",
            ContextLength = 131072
        },
        new ModelDefinition
        {
            Id = "x-ai/grok-4-fast",
            Name = "Grok 4 Fast",
            Provider = "xAI",
            SupportsReasoning = false,
            Description = "Speed-optimized Grok 4",
            ContextLength = 131072
        },
        new ModelDefinition
        {
            Id = "x-ai/grok-3",
            Name = "Grok 3",
            Provider = "xAI",
            SupportsReasoning = true,
            Description = "xAI flagship",
            ContextLength = 131072
        },

        // ============ Google Models ============
        new ModelDefinition
        {
            Id = "google/gemini-3-pro-preview",
            Name = "Gemini 3 Pro (Preview)",
            Provider = "Google",
            SupportsReasoning = true,
            Description = "Most advanced reasoning",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "google/gemini-3-flash-preview",
            Name = "Gemini 3 Flash (Preview)",
            Provider = "Google",
            SupportsReasoning = true,
            Description = "Fast multimodal",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "google/gemini-2.5-pro",
            Name = "Gemini 2.5 Pro",
            Provider = "Google",
            SupportsReasoning = true,
            Description = "Stable flagship",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "google/gemini-2.5-flash",
            Name = "Gemini 2.5 Flash",
            Provider = "Google",
            SupportsReasoning = true,
            Description = "Fast stable model",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "google/gemma-3-27b-it",
            Name = "Gemma 3 27B",
            Provider = "Google",
            SupportsReasoning = false,
            Description = "Open weights model",
            ContextLength = 8192
        },

        // ============ Moonshot (Kimi) Models ============
        new ModelDefinition
        {
            Id = "moonshotai/kimi-k2.5",
            Name = "Kimi K2.5",
            Provider = "Moonshot",
            SupportsReasoning = true,
            Description = "Agent Swarm multimodal",
            ContextLength = 128000
        },
        new ModelDefinition
        {
            Id = "moonshotai/kimi-k2-thinking",
            Name = "Kimi K2 Thinking",
            Provider = "Moonshot",
            SupportsReasoning = true,
            Description = "Long-horizon reasoning",
            ContextLength = 128000
        },

        // ============ DeepSeek Models ============
        new ModelDefinition
        {
            Id = "deepseek/deepseek-v3.2",
            Name = "DeepSeek V3.2",
            Provider = "DeepSeek",
            SupportsReasoning = false,
            Description = "Latest DeepSeek model",
            ContextLength = 128000
        },
        new ModelDefinition
        {
            Id = "deepseek/deepseek-r1",
            Name = "DeepSeek R1",
            Provider = "DeepSeek",
            SupportsReasoning = true,
            Description = "Advanced reasoning",
            ContextLength = 64000
        },
        new ModelDefinition
        {
            Id = "deepseek/deepseek-chat",
            Name = "DeepSeek Chat (V3)",
            Provider = "DeepSeek",
            SupportsReasoning = false,
            Description = "General purpose chat",
            ContextLength = 64000
        },

        // ============ Meta (Llama) Models ============
        new ModelDefinition
        {
            Id = "meta-llama/llama-4-maverick",
            Name = "Llama 4 Maverick",
            Provider = "Meta",
            SupportsReasoning = false,
            Description = "Latest Llama 4",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "meta-llama/llama-4-scout",
            Name = "Llama 4 Scout",
            Provider = "Meta",
            SupportsReasoning = false,
            Description = "Fast and efficient Llama 4",
            ContextLength = 500000
        },
        // Behemoth removed as not found in verified list

        // ============ Mistral Models ============
        new ModelDefinition
        {
            Id = "mistralai/mistral-large-2512",
            Name = "Mistral Large (2512)",
            Provider = "Mistral",
            SupportsReasoning = false,
            Description = "Latest Mistral Large",
            ContextLength = 128000
        },
        new ModelDefinition
        {
            Id = "mistralai/mistral-small-3",
            Name = "Mistral Small 3",
            Provider = "Mistral",
            SupportsReasoning = false,
            Description = "Latest Mistral Small",
            ContextLength = 128000
        },
        new ModelDefinition
        {
            Id = "mistralai/codestral-2508",
            Name = "Codestral (2508)",
            Provider = "Mistral",
            SupportsReasoning = false,
            Description = "Code specialized",
            ContextLength = 256000
        },

        // ============ Qwen Models ============
        new ModelDefinition
        {
            Id = "qwen/qwen3-max",
            Name = "Qwen 3 Max",
            Provider = "Qwen",
            SupportsReasoning = true,
            Description = "Most capable Qwen model",
            ContextLength = 131072
        },
        new ModelDefinition
        {
            Id = "qwen/qwq-32b",
            Name = "QwQ 32B",
            Provider = "Qwen",
            SupportsReasoning = true,
            Description = "Reasoning model",
            ContextLength = 131072
        },
         new ModelDefinition
        {
            Id = "qwen/qwen3-coder-plus",
            Name = "Qwen 3 Coder Plus",
            Provider = "Qwen",
            SupportsReasoning = false,
            Description = "Advanced coding model",
            ContextLength = 131072
        },

        // ============ Other ============
        new ModelDefinition
        {
            Id = "openrouter/auto",
            Name = "Auto Router",
            Provider = "OpenRouter",
            SupportsReasoning = false,
            Description = "Auto-selects best model",
            ContextLength = 128000
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
