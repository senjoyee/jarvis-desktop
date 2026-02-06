using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChloyeDesktop.Models;
using Microsoft.Extensions.Logging;

namespace ChloyeDesktop.Services;

public class McpManager
{
    private readonly ILogger<McpManager> _logger;
    private readonly DatabaseService _db;
    private readonly SecretsService _secrets;
    private readonly ConcurrentDictionary<Guid, McpConnection> _connections = new();
    private readonly HttpClient _httpClient = new();

    public McpManager(ILogger<McpManager> logger, DatabaseService db, SecretsService secrets)
    {
        _logger = logger;
        _db = db;
        _secrets = secrets;
        LoadConfigFile();
    }

    public string GetConfigFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChloyeDesktop",
            "mcp_config.json");
    }

    private string GetConfigPath() => GetConfigFilePath();

    private void LoadConfigFile()
    {
        var configPath = GetConfigPath();

        if (!File.Exists(configPath))
        {
            _logger.LogInformation("MCP config file not found at {Path}", configPath);
            return;
        }

        _logger.LogInformation("Loading MCP servers from config file: {Path}", configPath);
        // Config file is now the source of truth - no database storage needed
    }

    public async Task InitializeAsync()
    {
        var servers = ListServers();
        _logger.LogInformation("Auto-starting {Count} MCP servers...", servers.Count);
        
        foreach (var server in servers)
        {
            if (!server.Disabled)
            {
                // Fire and forget individual server starts so they don't block each other or startup
                _ = StartServer(server.Id);
            }
        }
    }

    #region Server Configuration CRUD

    public List<McpServerConfig> ListServers()
    {
        var configPath = GetConfigPath();
        if (!File.Exists(configPath))
        {
            return new List<McpServerConfig>();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("mcpServers", out var servers))
            {
                return new List<McpServerConfig>();
            }

            var serverList = new List<McpServerConfig>();
            foreach (var server in servers.EnumerateObject())
            {
                var name = server.Name;
                var config = server.Value;

                // Generate stable ID from server name
                var stableId = GenerateStableGuid(name);

                var serverConfig = new McpServerConfig
                {
                    Id = stableId,
                    Name = name,
                    Type = "local", // Default to local
                    Command = "",   // Default to empty
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (config.TryGetProperty("type", out var typeProp))
                {
                    var typeStr = typeProp.GetString()?.ToLower();
                    if (typeStr == "http" || typeStr == "remote")
                    {
                        serverConfig.Type = typeStr;
                    }
                }

                if (config.TryGetProperty("command", out var command))
                {
                    serverConfig.Command = command.GetString() ?? "";
                }

                if (config.TryGetProperty("url", out var url))
                {
                    serverConfig.Url = url.GetString();
                }

                if (config.TryGetProperty("args", out var args))
                {
                    var argsList = new List<string>();
                    foreach (var arg in args.EnumerateArray())
                    {
                        argsList.Add(arg.GetString()!);
                    }
                    serverConfig.ArgsJson = JsonSerializer.Serialize(argsList);
                }

                if (config.TryGetProperty("cwd", out var cwd))
                {
                    serverConfig.Cwd = cwd.GetString();
                }

                if (config.TryGetProperty("env", out var env))
                {
                    serverConfig.EnvJson = env.GetRawText();
                }

                if (config.TryGetProperty("disabled", out var disabled))
                {
                    serverConfig.Disabled = disabled.GetBoolean();
                }

                serverList.Add(serverConfig);
            }

            return serverList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read MCP config file");
            return new List<McpServerConfig>();
        }
    }

    private Guid GenerateStableGuid(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }

    public McpServerConfig AddServer(McpServerConfig config)
    {
        // Config file is read-only from the app - users edit it directly
        _logger.LogWarning("AddServer called but config file is read-only. Edit mcp_config.json directly.");
        return config;
    }

    public bool RemoveServer(Guid id)
    {
        // Config file is read-only from the app - users edit it directly
        StopServer(id);
        _logger.LogWarning("RemoveServer called but config file is read-only. Edit mcp_config.json directly.");
        return false;
    }

    #endregion

    #region Connection Management

    public McpConnectionStatus GetStatus(Guid serverId)
    {
        if (_connections.TryGetValue(serverId, out var conn))
        {
            return conn.Status;
        }
        return McpConnectionStatus.Stopped;
    }

    public List<string> GetLogs(Guid serverId, int maxLines = 100)
    {
        if (_connections.TryGetValue(serverId, out var conn))
        {
            return conn.Logs.TakeLast(maxLines).ToList();
        }
        return new List<string>();
    }

    public async Task<bool> StartServer(Guid serverId)
    {
        _logger.LogInformation("StartServer called for ID: {ServerId}", serverId);
        var server = ListServers().FirstOrDefault(s => s.Id == serverId);
        if (server == null)
        {
            _logger.LogError("Server not found: {ServerId}", serverId);
            return false;
        }

        _logger.LogInformation("Starting {Type} server: {Name}", server.Type, server.Name);

        if (server.Type == "local")
        {
            return await StartLocalServer(server);
        }
        else if (server.Type == "remote")
        {
            return await ConnectRemoteServer(server);
        }
        else if (server.Type == "http")
        {
            return await ConnectHttpServer(server);
        }

        return false;
    }

    public void StopServer(Guid serverId)
    {
        if (_connections.TryRemove(serverId, out var conn))
        {
            conn.Dispose();
            _logger.LogInformation("Stopped MCP server: {Id}", serverId);
        }
    }

    private async Task<bool> StartLocalServer(McpServerConfig config)
    {
        if (string.IsNullOrEmpty(config.Command))
        {
            _logger.LogError("Local MCP server has no command configured");
            return false;
        }

        // Check if already running
        if (_connections.ContainsKey(config.Id))
        {
            _logger.LogWarning("Server {Name} is already running or starting", config.Name);
            return false;
        }

        _logger.LogInformation("Creating connection for {Name}", config.Name);
        var connection = new McpLocalConnection(config, _logger);
        _connections[config.Id] = connection;

        try
        {
            _logger.LogInformation("Calling StartAsync for {Name}", config.Name);
            await connection.StartAsync();
            _logger.LogInformation("StartAsync completed successfully for {Name}", config.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start local MCP server: {Name}", config.Name);
            _connections.TryRemove(config.Id, out _);
            return false;
        }
    }

    private async Task<bool> ConnectRemoteServer(McpServerConfig config)
    {
        if (string.IsNullOrEmpty(config.Url))
        {
            _logger.LogError("Remote MCP server has no URL configured");
            return false;
        }

        string? authToken = null;
        if (config.AuthType == "bearer" && !string.IsNullOrEmpty(config.AuthRef))
        {
            authToken = _secrets.GetSecret($"MCP/{config.AuthRef}");
        }

        var connection = new McpRemoteConnection(config, authToken, _httpClient, _logger);
        _connections[config.Id] = connection;

        try
        {
            await connection.ConnectAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to remote MCP server: {Name}", config.Name);
            _connections.TryRemove(config.Id, out _);
            return false;
        }
    }

    private async Task<bool> ConnectHttpServer(McpServerConfig config)
    {
        try
        {
            if (string.IsNullOrEmpty(config.Url))
            {
                _logger.LogError("HTTP MCP server has no URL configured");
                return false;
            }

            var connection = new McpHttpConnection(config, _httpClient, _logger);
            _connections[config.Id] = connection; // Store connection
            
            await connection.ConnectAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to HTTP server: {Name}", config.Name);
            _connections.TryRemove(config.Id, out _);
            return false;
        }
    }

    #endregion

    #region Tool Operations

    public async Task<List<McpTool>> ListToolsAsync(Guid serverId)
    {
        if (!_connections.TryGetValue(serverId, out var conn))
        {
            throw new InvalidOperationException("Server not connected");
        }

        return await conn.ListToolsAsync().ConfigureAwait(false);
    }

    public async Task<JsonElement> CallToolAsync(Guid serverId, string toolName, JsonElement args)
    {
        if (!_connections.TryGetValue(serverId, out var conn))
        {
            throw new InvalidOperationException("Server not connected");
        }

        return await conn.CallToolAsync(toolName, args).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all tools from all connected MCP servers.
    /// Returns tools with their server IDs for routing calls.
    /// </summary>
    public async Task<List<McpToolWithServer>> GetAllToolsAsync()
    {
        var result = new List<McpToolWithServer>();
        var connectedServers = _connections
            .Where(c => c.Value.Status == McpConnectionStatus.Connected)
            .ToList();

        foreach (var (serverId, conn) in connectedServers)
        {
            try
            {
                var serverConfig = ListServers().FirstOrDefault(s => s.Id == serverId);
                var serverName = serverConfig?.Name ?? serverId.ToString();
                
                var tools = await conn.ListToolsAsync().ConfigureAwait(false);
                foreach (var tool in tools)
                {
                    result.Add(new McpToolWithServer
                    {
                        ServerId = serverId,
                        ServerName = serverName,
                        Tool = tool
                    });
                }
                _logger.LogDebug("Loaded {Count} tools from server {Name}", tools.Count, serverName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list tools from server {ServerId}", serverId);
            }
        }

        return result;
    }

    /// <summary>
    /// Calls a tool and returns the result, routing to the correct server.
    /// </summary>
    public async Task<JsonElement> CallToolByNameAsync(string toolName, JsonElement args)
    {
        // Find which server has this tool
        var allTools = await GetAllToolsAsync().ConfigureAwait(false);
        var toolWithServer = allTools.FirstOrDefault(t => t.Tool.Name == toolName);
        
        if (toolWithServer == null)
        {
            throw new InvalidOperationException($"Tool not found: {toolName}");
        }

        return await CallToolAsync(toolWithServer.ServerId, toolName, args).ConfigureAwait(false);
    }

    #endregion
}

public enum McpConnectionStatus
{
    Stopped,
    Connecting,
    Connected,
    Error
}

public class McpTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("inputSchema")]
    public JsonElement? InputSchema { get; set; }
}

/// <summary>
/// A tool with its associated server information for routing calls.
/// </summary>
public class McpToolWithServer
{
    public Guid ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public McpTool Tool { get; set; } = new();
}

public abstract class McpConnection : IDisposable
{
    protected readonly McpServerConfig Config;
    protected readonly ILogger Logger;
    protected readonly List<string> _logs = new();
    protected int _requestId = 0;

    public McpConnectionStatus Status { get; protected set; } = McpConnectionStatus.Stopped;
    public IReadOnlyList<string> Logs => _logs;

    protected McpConnection(McpServerConfig config, ILogger logger)
    {
        Config = config;
        Logger = logger;
    }

    protected void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var logMessage = $"[{timestamp}] {message}";
        _logs.Add(logMessage);
        Logger.LogDebug("[{ServerName}] {Message}", Config.Name, message);
        if (_logs.Count > 1000)
        {
            _logs.RemoveAt(0);
        }
    }

    public abstract Task<List<McpTool>> ListToolsAsync();
    public abstract Task<JsonElement> CallToolAsync(string toolName, JsonElement args);
    public abstract void Dispose();
}

public class McpLocalConnection : McpConnection
{
    private Process? _process;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();

    public McpLocalConnection(McpServerConfig config, ILogger logger) : base(config, logger)
    {
    }

    public async Task StartAsync()
    {
        Status = McpConnectionStatus.Connecting;
        AddLog($"Starting: {Config.Command}");

        var args = string.Empty;
        if (!string.IsNullOrEmpty(Config.ArgsJson))
        {
            var argsList = JsonSerializer.Deserialize<List<string>>(Config.ArgsJson);
            if (argsList != null && argsList.Count > 0)
            {
                // Properly quote arguments that contain spaces
                var quotedArgs = argsList.Select(arg => 
                    arg.Contains(' ') && !arg.StartsWith("\"") ? $"\"{arg}\"" : arg);
                args = string.Join(" ", quotedArgs);
            }
        }

        AddLog($"Arguments: {args}");

        var startInfo = new ProcessStartInfo
        {
            FileName = Config.Command,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Config.Cwd ?? Environment.CurrentDirectory
        };

        if (!string.IsNullOrEmpty(Config.EnvJson))
        {
            var env = JsonSerializer.Deserialize<Dictionary<string, string>>(Config.EnvJson);
            if (env != null)
            {
                foreach (var (key, value) in env)
                {
                    startInfo.EnvironmentVariables[key] = value;
                }
            }
        }

        _process = new Process { StartInfo = startInfo };
        _process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                HandleOutput(e.Data);
            }
        };
        _process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                AddLog($"[stderr] {e.Data}");
            }
        };

        AddLog($"Starting process: {startInfo.FileName} {startInfo.Arguments}");
        _process.Start();
        AddLog($"Process started with PID: {_process.Id}");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        AddLog("Waiting for output...");

        await InitializeAsync().ConfigureAwait(false);
        Status = McpConnectionStatus.Connected;
        AddLog("Connected");
    }

    private async Task InitializeAsync()
    {
        AddLog("Sending initialize request...");
        var response = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "ChloyeDesktop", version = "1.0.0" }
        });

        AddLog("Received initialize response");
        await SendNotificationAsync("notifications/initialized", new { }).ConfigureAwait(false);
    }

    private void HandleOutput(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        // Skip non-JSON lines (e.g., server startup messages)
        if (!line.TrimStart().StartsWith("{"))
        {
            AddLog($"[info] {line}");
            return;
        }

        AddLog($"[json] {line}");
        try
        {
            var json = JsonDocument.Parse(line);
            var root = json.RootElement;

            if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
            {
                var id = idProp.GetInt32();
                if (_pendingRequests.TryRemove(id, out var tcs))
                {
                    if (root.TryGetProperty("error", out var error))
                    {
                        tcs.SetException(new Exception(error.GetProperty("message").GetString()));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        tcs.SetResult(result.Clone());
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            AddLog($"[parse-error] {ex.Message}: {line}");
        }
    }

    private async Task<JsonElement> SendRequestAsync(string method, object? parameters)
    {
        var id = Interlocked.Increment(ref _requestId);
        var tcs = new TaskCompletionSource<JsonElement>();
        _pendingRequests[id] = tcs;

        var request = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(request);
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _process!.StandardInput.WriteLineAsync(json).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        cts.Token.Register(() => tcs.TrySetCanceled());

        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task SendNotificationAsync(string method, object? parameters)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(notification);
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _process!.StandardInput.WriteLineAsync(json).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public override async Task<List<McpTool>> ListToolsAsync()
    {
        var result = await SendRequestAsync("tools/list", new { }).ConfigureAwait(false);
        var tools = result.GetProperty("tools").Deserialize<List<McpTool>>();
        return tools ?? new List<McpTool>();
    }

    public override async Task<JsonElement> CallToolAsync(string toolName, JsonElement args)
    {
        AddLog($"Calling tool: {toolName}");
        var result = await SendRequestAsync("tools/call", new
        {
            name = toolName,
            arguments = args
        }).ConfigureAwait(false);
        return result;
    }

    public override void Dispose()
    {
        Status = McpConnectionStatus.Stopped;
        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.Kill();
            }
            catch { }
        }
        _process?.Dispose();
        _writeLock.Dispose();
    }
}

public class McpRemoteConnection : McpConnection
{
    private readonly string? _authToken;
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _sseCts;
    private string? _sessionId;

    public McpRemoteConnection(McpServerConfig config, string? authToken, HttpClient httpClient, ILogger logger)
        : base(config, logger)
    {
        _authToken = authToken;
        _httpClient = httpClient;
    }

    public async Task ConnectAsync()
    {
        Status = McpConnectionStatus.Connecting;
        AddLog($"Connecting to: {Config.Url}");

        _sseCts = new CancellationTokenSource();

        // Start SSE connection
        _ = Task.Run(() => ListenToSseAsync(_sseCts.Token));

        // Wait briefly for connection
        await Task.Delay(500);

        await InitializeAsync();
        Status = McpConnectionStatus.Connected;
        AddLog("Connected");
    }

    private async Task ListenToSseAsync(CancellationToken ct)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{Config.Url}/sse");
            if (!string.IsNullOrEmpty(_authToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authToken);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (!ct.IsCancellationRequested && !reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("event:"))
                {
                    var eventType = line[6..].Trim();
                    var dataLine = await reader.ReadLineAsync(ct);
                    if (dataLine?.StartsWith("data:") == true)
                    {
                        var data = dataLine[5..].Trim();
                        HandleSseEvent(eventType, data);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            AddLog($"SSE error: {ex.Message}");
            Status = McpConnectionStatus.Error;
        }
    }

    private void HandleSseEvent(string eventType, string data)
    {
        AddLog($"[SSE] {eventType}: {data}");
        
        if (eventType == "endpoint")
        {
            _sessionId = data;
        }
    }

    private async Task InitializeAsync()
    {
        await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "ChloyeDesktop", version = "1.0.0" }
        });

        await SendRequestAsync("notifications/initialized", new { });
    }

    private async Task<JsonElement> SendRequestAsync(string method, object? parameters)
    {
        var id = Interlocked.Increment(ref _requestId);

        var request = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{Config.Url}/message");
        if (!string.IsNullOrEmpty(_authToken))
        {
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authToken);
        }
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseBody);
        
        if (json.RootElement.TryGetProperty("error", out var error))
        {
            throw new Exception(error.GetProperty("message").GetString());
        }

        return json.RootElement.GetProperty("result").Clone();
    }

    public override async Task<List<McpTool>> ListToolsAsync()
    {
        var result = await SendRequestAsync("tools/list", new { });
        var tools = result.GetProperty("tools").Deserialize<List<McpTool>>();
        return tools ?? new List<McpTool>();
    }

    public override async Task<JsonElement> CallToolAsync(string toolName, JsonElement args)
    {
        AddLog($"Calling tool: {toolName}");
        var result = await SendRequestAsync("tools/call", new
        {
            name = toolName,
            arguments = args
        });
        return result;
    }

    public override void Dispose()
    {
        Status = McpConnectionStatus.Stopped;
        _sseCts?.Cancel();
        _sseCts?.Dispose();
    }
}
