using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ChloyeDesktop.Services;

public class DatabaseService : IDisposable
{
    private readonly ILogger<DatabaseService> _logger;
    private readonly string _connectionString;

    public DatabaseService(ILogger<DatabaseService> logger)
    {
        _logger = logger;
        var dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChloyeDesktop");
        Directory.CreateDirectory(dataPath);
        
        var dbPath = Path.Combine(dataPath, "chloye.db");
        _connectionString = $"Data Source={dbPath}";
        
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = GetConnection();
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS conversations (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY,
                conversation_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                model TEXT NOT NULL,
                created_at TEXT NOT NULL,
                metadata_json TEXT,
                FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_messages_conversation 
            ON messages(conversation_id);

            CREATE TABLE IF NOT EXISTS mcp_servers (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                type TEXT NOT NULL,
                command TEXT,
                args_json TEXT,
                cwd TEXT,
                env_json TEXT,
                auto_start INTEGER DEFAULT 0,
                url TEXT,
                auth_type TEXT,
                auth_ref TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();

        _logger.LogInformation("Database initialized");
    }

    public SqliteConnection GetConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    public void Dispose()
    {
        // Connection is created per-use via GetConnection()
    }
}
