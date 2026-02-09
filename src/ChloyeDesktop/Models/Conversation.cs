namespace ChloyeDesktop.Models;

public class Conversation
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsPinned { get; set; }
}

public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = string.Empty; // user, assistant, system
    public string Content { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? MetadataJson { get; set; }
}

public class McpServerConfig
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // local, remote
    
    // Local server fields
    public string? Command { get; set; }
    public string? ArgsJson { get; set; }
    public string? Cwd { get; set; }
    public string? EnvJson { get; set; }
    public bool AutoStart { get; set; }
    
    // Remote server fields
    public string? Url { get; set; }
    public string? AuthType { get; set; } // none, bearer
    public string? AuthRef { get; set; } // credential manager key
    public bool Disabled { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
