using ChloyeDesktop.Models;
using Microsoft.Extensions.Logging;

namespace ChloyeDesktop.Services;

public class ConversationService
{
    private readonly ILogger<ConversationService> _logger;
    private readonly DatabaseService _db;

    public ConversationService(ILogger<ConversationService> logger, DatabaseService db)
    {
        _logger = logger;
        _db = db;
    }

    public List<Conversation> ListConversations()
    {
        using var conn = _db.GetConnection();
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, created_at, updated_at, is_pinned FROM conversations ORDER BY updated_at DESC";

        var conversations = new List<Conversation>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conversations.Add(new Conversation
            {
                Id = Guid.Parse(reader.GetString(0)),
                Title = reader.GetString(1),
                CreatedAt = DateTime.Parse(reader.GetString(2)),
                UpdatedAt = DateTime.Parse(reader.GetString(3)),
                IsPinned = !reader.IsDBNull(4) && reader.GetInt32(4) == 1
            });
        }

        return conversations;
    }

    public Conversation CreateConversation(string title)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsPinned = false
        };

        using var conn = _db.GetConnection();
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO conversations (id, title, created_at, updated_at, is_pinned)
            VALUES ($id, $title, $created_at, $updated_at, $is_pinned)";
        cmd.Parameters.AddWithValue("$id", conversation.Id.ToString());
        cmd.Parameters.AddWithValue("$title", conversation.Title);
        cmd.Parameters.AddWithValue("$created_at", conversation.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated_at", conversation.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$is_pinned", conversation.IsPinned ? 1 : 0);
        cmd.ExecuteNonQuery();

        _logger.LogInformation("Created conversation: {Id}", conversation.Id);
        return conversation;
    }

    public bool DeleteConversation(Guid id)
    {
        using var conn = _db.GetConnection();
        conn.Open();

        using var transaction = conn.BeginTransaction();
        
        var deleteMessages = conn.CreateCommand();
        deleteMessages.CommandText = "DELETE FROM messages WHERE conversation_id = $id";
        deleteMessages.Parameters.AddWithValue("$id", id.ToString());
        deleteMessages.ExecuteNonQuery();

        var deleteConv = conn.CreateCommand();
        deleteConv.CommandText = "DELETE FROM conversations WHERE id = $id";
        deleteConv.Parameters.AddWithValue("$id", id.ToString());
        var rows = deleteConv.ExecuteNonQuery();

        transaction.Commit();

        _logger.LogInformation("Deleted conversation: {Id}", id);
        return rows > 0;
    }

    public bool RenameConversation(Guid id, string newTitle)
    {
        using var conn = _db.GetConnection();
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE conversations 
            SET title = $title, updated_at = $updated_at 
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$title", newTitle);
        cmd.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("O"));
        
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool TogglePinConversation(Guid id, bool isPinned)
    {
        using var conn = _db.GetConnection();
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE conversations 
            SET is_pinned = $is_pinned, updated_at = $updated_at 
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$is_pinned", isPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("O"));
        
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<Message> GetMessages(Guid conversationId)
    {
        using var conn = _db.GetConnection();
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, conversation_id, role, content, model, created_at, metadata_json 
            FROM messages 
            WHERE conversation_id = $conversation_id 
            ORDER BY created_at ASC";
        cmd.Parameters.AddWithValue("$conversation_id", conversationId.ToString());

        var messages = new List<Message>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            messages.Add(new Message
            {
                Id = Guid.Parse(reader.GetString(0)),
                ConversationId = Guid.Parse(reader.GetString(1)),
                Role = reader.GetString(2),
                Content = reader.GetString(3),
                Model = reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                MetadataJson = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return messages;
    }

    public Message AddMessage(Guid conversationId, string role, string content, string model)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Model = model,
            CreatedAt = DateTime.UtcNow
        };

        using var conn = _db.GetConnection();
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO messages (id, conversation_id, role, content, model, created_at)
            VALUES ($id, $conversation_id, $role, $content, $model, $created_at)";
        cmd.Parameters.AddWithValue("$id", message.Id.ToString());
        cmd.Parameters.AddWithValue("$conversation_id", message.ConversationId.ToString());
        cmd.Parameters.AddWithValue("$role", message.Role);
        cmd.Parameters.AddWithValue("$content", message.Content);
        cmd.Parameters.AddWithValue("$model", message.Model);
        cmd.Parameters.AddWithValue("$created_at", message.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();

        // Update conversation timestamp
        var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE conversations SET updated_at = $updated_at WHERE id = $id";
        updateCmd.Parameters.AddWithValue("$id", conversationId.ToString());
        updateCmd.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("O"));
        updateCmd.ExecuteNonQuery();

        return message;
    }

    public void UpdateMessageContent(Guid messageId, string content)
    {
        using var conn = _db.GetConnection();
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE messages SET content = $content WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", messageId.ToString());
        cmd.Parameters.AddWithValue("$content", content);
        cmd.ExecuteNonQuery();
    }
}
