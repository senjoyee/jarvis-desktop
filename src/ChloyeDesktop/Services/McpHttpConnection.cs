using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChloyeDesktop.Models; // Ensure namespace correct for McpServerConfig/McpTool
using Microsoft.Extensions.Logging;

namespace ChloyeDesktop.Services;

public class McpHttpConnection : McpConnection
{
    private readonly HttpClient _httpClient;
    private string? _sessionId;
    
    public McpHttpConnection(McpServerConfig config, HttpClient httpClient, ILogger logger) 
        : base(config, logger)
    {
        _httpClient = httpClient;
    }

    public async Task ConnectAsync()
    {
        Status = McpConnectionStatus.Connecting;
        AddLog($"Connecting (HTTP) to: {Config.Url}");

        try
        {
            // Send initialize request
            var initResult = await SendRpcAsync("initialize", new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "ChloyeDesktop", version = "1.0.0" }
            }).ConfigureAwait(false);

            // Send initialized notification
            try
            {
                await SendRpcAsync("notifications/initialized", new { }).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Ignore errors for initialized notification as some servers (like Microsoft Learn) don't support it
                AddLog("Warning: Server rejected initialized notification, continuing...");
            }

            Status = McpConnectionStatus.Connected;
            AddLog("Connected (Stateless HTTP)");
        }
        catch (Exception ex)
        {
            Status = McpConnectionStatus.Error;
            AddLog($"Connection error: {ex.Message}");
            throw;
        }
    }

    public override async Task<List<McpTool>> ListToolsAsync()
    {
        try 
        {
            var result = await SendRpcAsync("tools/list", new { }).ConfigureAwait(false);
            if (result.ValueKind != JsonValueKind.Undefined && result.TryGetProperty("tools", out var toolsArray))
            {
                var tools = JsonSerializer.Deserialize<List<McpTool>>(toolsArray.GetRawText(), 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return tools ?? new List<McpTool>();
            }
        }
        catch (Exception ex)
        {
            AddLog($"ListTools error: {ex.Message}");
        }
        return new List<McpTool>();
    }

    public override async Task<JsonElement> CallToolAsync(string toolName, JsonElement args)
    {
        var result = await SendRpcAsync("tools/call", new 
        { 
            name = toolName, 
            arguments = args 
        }).ConfigureAwait(false);
        
        // MCP tool call result wrapper? 
        // Spec says result is { content: [...] }
        return result;
    }

    public override void Dispose()
    {
        // Stateless, nothing to dispose (HttpClient is injected)
        Status = McpConnectionStatus.Stopped;
    }

    private async Task<JsonElement> SendRpcAsync(string method, object? parameters)
    {
        var id = Interlocked.Increment(ref _requestId);
        var requestPayload = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        };

        var request = new HttpRequestMessage(HttpMethod.Post, Config.Url);
        request.Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");
        
        // CRITICAL HEADERS for this transport
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (!string.IsNullOrEmpty(_sessionId))
        {
            request.Headers.Add("mcp-session-id", _sessionId);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
             var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
             AddLog($"HTTP Error {response.StatusCode}: {errorContent}");
        }

        response.EnsureSuccessStatusCode();

        // Check for session ID in response headers
        // Check for session ID in response headers
        if (response.Headers.TryGetValues("mcp-session-id", out var sessionIds))
        {
            var newSessionId = System.Linq.Enumerable.FirstOrDefault(sessionIds);
            if (!string.IsNullOrEmpty(newSessionId))
            {
                if (_sessionId != newSessionId)
                {
                    AddLog($"[Session] Updated ID: {newSessionId}");
                }
                _sessionId = newSessionId;
            }
        }
        else
        {
             // AddLog("[Session] Warning: No session ID in response");
        }

        // Parse Response based on Content-Type
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        AddLog($"[Debug] Response Content-Type: {mediaType}");

        if (mediaType != null && mediaType.Contains("application/json"))
        {
            // Direct JSON response
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AddLog($"[Debug] JSON Body: {json}");
            
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
             if (root.TryGetProperty("result", out var resultProp))
            {
                return resultProp.Clone();
            }
             else if (root.TryGetProperty("error", out var errorProp))
            {
                throw new Exception($"MCP Error: {errorProp.GetRawText()}");
            }
            return default;
        }

        // SSE Response
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        // AddLog("[Debug] Parsing SSE...");

        JsonElement finalResult = default;
        bool foundResult = false;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) continue;

            if (!line.StartsWith("event:"))
            {
                 AddLog($"[Debug] SSE Ignored: {line}");
            }

            if (line.StartsWith("event:"))
            {
                var eventType = line[6..].Trim();
                var dataLine = await reader.ReadLineAsync().ConfigureAwait(false);
                
                if (dataLine?.StartsWith("data:") == true)
                {
                    var data = dataLine[5..].Trim();
                    // AddLog($"[SSE] {eventType}: {data}"); 

                    if (eventType == "message")
                    {
                        ProcessMessage(data, ref finalResult, ref foundResult);
                    }
                }
            }
            else if (line.StartsWith("data:"))
            {
                // Handle implicit "message" event (spec compliant)
                var data = line[5..].Trim();
                AddLog($"[SSE] (Implicit) message: {data}");
                ProcessMessage(data, ref finalResult, ref foundResult);
            }
        }

        if (foundResult)
        {
            return finalResult;
        }
        
        return default; 
    }

    private void ProcessMessage(string data, ref JsonElement finalResult, ref bool foundResult)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("result", out var resultProp))
            {
                finalResult = resultProp.Clone();
                foundResult = true;
            }
            else if (root.TryGetProperty("error", out var errorProp))
            {
                throw new Exception($"MCP Error: {errorProp.GetRawText()}");
            }
        }
        catch (Exception ex)
        {
            AddLog($"Error parsing message: {ex.Message}");
        }
    }


}
