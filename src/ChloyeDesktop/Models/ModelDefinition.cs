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
/// Static catalog of available models through OpenRouter (Updated February 2026)
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
            Description = "Latest flagship model with enhanced agentic and long context performance",
            ContextLength = 400000
        },
        new ModelDefinition
        {
            Id = "openai/gpt-5.2-thinking",
            Name = "GPT-5.2 Thinking",
            Provider = "OpenAI",
            SupportsReasoning = true,
            Description = "GPT-5.2 with adaptive reasoning for complex tasks",
            ContextLength = 400000
        },
        new ModelDefinition
        {
            Id = "openai/gpt-5.2-pro",
            Name = "GPT-5.2 Pro",
            Provider = "OpenAI",
            SupportsReasoning = true,
            Description = "Most capable GPT-5.2 model for demanding workloads",
            ContextLength = 400000
        },
        new ModelDefinition
        {
            Id = "openai/gpt-5.3-codex",
            Name = "GPT-5.3 Codex",
            Provider = "OpenAI",
            SupportsReasoning = false,
            Description = "Specialized for code generation and software engineering",
            ContextLength = 256000
        },
        new ModelDefinition
        {
            Id = "openai/gpt-5-mini",
            Name = "GPT-5 Mini",
            Provider = "OpenAI",
            SupportsReasoning = false,
            Description = "Fast and cost-effective GPT-5 variant",
            ContextLength = 200000
        },
        new ModelDefinition
        {
            Id = "openai/gpt-5-nano",
            Name = "GPT-5 Nano",
            Provider = "OpenAI",
            SupportsReasoning = false,
            Description = "Ultra-lightweight model for quick responses",
            ContextLength = 128000
        },

        // ============ Anthropic Models ============
        new ModelDefinition
        {
            Id = "anthropic/claude-opus-4.6",
            Name = "Claude Opus 4.6",
            Provider = "Anthropic",
            SupportsReasoning = true,
            Description = "State-of-the-art for agentic coding, reasoning & knowledge work (Feb 2026)",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "anthropic/claude-sonnet-4.5",
            Name = "Claude Sonnet 4.5",
            Provider = "Anthropic",
            SupportsReasoning = true,
            Description = "Optimized for agentic workflows and coding tasks",
            ContextLength = 200000
        },
        new ModelDefinition
        {
            Id = "anthropic/claude-sonnet-4",
            Name = "Claude Sonnet 4",
            Provider = "Anthropic",
            SupportsReasoning = false,
            Description = "Balanced performance and speed",
            ContextLength = 200000
        },
        new ModelDefinition
        {
            Id = "anthropic/claude-haiku-4",
            Name = "Claude Haiku 4",
            Provider = "Anthropic",
            SupportsReasoning = false,
            Description = "Fast and affordable for high-volume tasks",
            ContextLength = 200000
        },

        // ============ Google Models ============
        new ModelDefinition
        {
            Id = "google/gemini-3-pro",
            Name = "Gemini 3 Pro",
            Provider = "Google",
            SupportsReasoning = true,
            Description = "Most advanced reasoning across text, images, audio, video & code",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "google/gemini-3-flash",
            Name = "Gemini 3 Flash",
            Provider = "Google",
            SupportsReasoning = true,
            Description = "Fast multimodal with impressive reasoning",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "google/gemini-2.5-pro",
            Name = "Gemini 2.5 Pro",
            Provider = "Google",
            SupportsReasoning = true,
            Description = "Previous generation flagship with thinking",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "google/gemini-2.5-flash",
            Name = "Gemini 2.5 Flash",
            Provider = "Google",
            SupportsReasoning = true,
            Description = "Fast Gemini with thinking support",
            ContextLength = 1000000
        },

        // ============ Moonshot (Kimi) Models ============
        new ModelDefinition
        {
            Id = "moonshotai/kimi-k2.5",
            Name = "Kimi K2.5",
            Provider = "Moonshot",
            SupportsReasoning = true,
            Description = "Open-source multimodal agent with Agent Swarm technology (Jan 2026)",
            ContextLength = 128000
        },
        new ModelDefinition
        {
            Id = "moonshotai/kimi-k2",
            Name = "Kimi K2",
            Provider = "Moonshot",
            SupportsReasoning = false,
            Description = "Strong agentic capabilities with tool use",
            ContextLength = 128000
        },
        new ModelDefinition
        {
            Id = "moonshotai/kimi-k2-thinking",
            Name = "Kimi K2 Thinking",
            Provider = "Moonshot",
            SupportsReasoning = true,
            Description = "Advanced reasoning optimized for long-horizon tasks",
            ContextLength = 128000
        },

        // ============ DeepSeek Models ============
        new ModelDefinition
        {
            Id = "deepseek/deepseek-v3.2",
            Name = "DeepSeek V3.2",
            Provider = "DeepSeek",
            SupportsReasoning = false,
            Description = "Latest DeepSeek with enhanced capabilities",
            ContextLength = 128000
        },
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

        // ============ Meta (Llama) Models ============
        new ModelDefinition
        {
            Id = "meta-llama/llama-4-maverick",
            Name = "Llama 4 Maverick",
            Provider = "Meta",
            SupportsReasoning = false,
            Description = "Latest Llama 4 with enhanced capabilities",
            ContextLength = 1000000
        },
        new ModelDefinition
        {
            Id = "meta-llama/llama-4-scout",
            Name = "Llama 4 Scout",
            Provider = "Meta",
            SupportsReasoning = false,
            Description = "Fast and efficient Llama 4 variant",
            ContextLength = 500000
        },
        new ModelDefinition
        {
            Id = "meta-llama/llama-4-behemoth",
            Name = "Llama 4 Behemoth",
            Provider = "Meta",
            SupportsReasoning = true,
            Description = "2T parameter 'teacher' model - most intelligent open LLM",
            ContextLength = 1000000
        },

        // ============ xAI (Grok) Models ============
        new ModelDefinition
        {
            Id = "x-ai/grok-3",
            Name = "Grok 3",
            Provider = "xAI",
            SupportsReasoning = true,
            Description = "xAI's flagship model with advanced reasoning",
            ContextLength = 131072
        },
        new ModelDefinition
        {
            Id = "x-ai/grok-3-fast",
            Name = "Grok 3 Fast",
            Provider = "xAI",
            SupportsReasoning = false,
            Description = "Speed-optimized Grok 3 variant",
            ContextLength = 131072
        },

        // ============ Mistral Models ============
        new ModelDefinition
        {
            Id = "mistralai/mistral-large-2501",
            Name = "Mistral Large",
            Provider = "Mistral",
            SupportsReasoning = false,
            Description = "Flagship Mistral model (Jan 2025)",
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
        new ModelDefinition
        {
            Id = "mistralai/mistral-small-2501",
            Name = "Mistral Small",
            Provider = "Mistral",
            SupportsReasoning = false,
            Description = "Cost-effective for simpler tasks",
            ContextLength = 128000
        },

        // ============ Qwen Models ============
        new ModelDefinition
        {
            Id = "qwen/qwen-3-235b",
            Name = "Qwen 3 235B",
            Provider = "Qwen",
            SupportsReasoning = true,
            Description = "Alibaba's most capable model",
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
        },
        new ModelDefinition
        {
            Id = "qwen/qwen-2.5-coder-32b",
            Name = "Qwen 2.5 Coder 32B",
            Provider = "Qwen",
            SupportsReasoning = false,
            Description = "Specialized for code tasks",
            ContextLength = 131072
        },

        // ============ Other Notable Models ============
        new ModelDefinition
        {
            Id = "black-forest-labs/flux-2-pro",
            Name = "FLUX 2 Pro",
            Provider = "Black Forest",
            SupportsReasoning = false,
            Description = "State-of-the-art image generation",
            ContextLength = 8192
        },
        new ModelDefinition
        {
            Id = "openrouter/auto",
            Name = "Auto (Best for prompt)",
            Provider = "OpenRouter",
            SupportsReasoning = false,
            Description = "Automatically selects the best model for your prompt",
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
