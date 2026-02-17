using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ChloyeDesktop.Services;

/// <summary>
/// Implements the "Code Mode" pattern from the Anthropic engineering blog.
/// Instead of loading all MCP tool definitions upfront, the LLM writes TypeScript code
/// that calls MCP tools via generated wrapper files. This reduces token usage dramatically.
///
/// Architecture:
///   1. GenerateToolFilesAsync — creates ./servers/{name}/{tool}.ts wrappers in a temp dir
///   2. ExecuteCodeAsync — runs LLM-generated TypeScript in a sandboxed Node.js process
///   3. The sandbox communicates with .NET via stdin/stdout JSON-RPC for actual MCP calls
/// </summary>
public class CodeExecutionService
{
    private readonly ILogger<CodeExecutionService> _logger;
    private readonly McpManager _mcp;
    private string? _workspaceDir;
    private readonly int _timeoutSeconds;

    public CodeExecutionService(ILogger<CodeExecutionService> logger, McpManager mcp)
    {
        _logger = logger;
        _mcp = mcp;
        _timeoutSeconds = 120;
    }

    /// <summary>
    /// Gets the current workspace directory where tool files are generated.
    /// Returns null if not yet generated.
    /// </summary>
    public string? WorkspaceDir => _workspaceDir;

    /// <summary>
    /// Generates TypeScript wrapper files for all connected MCP servers and their tools.
    /// Creates a file tree like:
    ///   workspace/
    ///     __mcp_bridge.ts        ← bridge module for calling MCP tools
    ///     servers/
    ///       server-name/
    ///         toolName.ts        ← individual tool wrapper
    ///         index.ts           ← re-exports all tools
    /// </summary>
    public async Task<string> GenerateToolFilesAsync()
    {
        // Create a temp workspace
        _workspaceDir = Path.Combine(Path.GetTempPath(), "jarvis-code-mode", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workspaceDir);
        _logger.LogInformation("Code Mode workspace: {Dir}", _workspaceDir);

        // Write the bridge module
        var bridgePath = Path.Combine(_workspaceDir, "__mcp_bridge.ts");
        await File.WriteAllTextAsync(bridgePath, GenerateBridgeModule());

        // Write a tsconfig for the workspace
        var tsconfigPath = Path.Combine(_workspaceDir, "tsconfig.json");
        await File.WriteAllTextAsync(tsconfigPath, @"{
  ""compilerOptions"": {
    ""target"": ""ES2022"",
    ""module"": ""NodeNext"",
    ""moduleResolution"": ""NodeNext"",
    ""esModuleInterop"": true,
    ""strict"": false,
    ""outDir"": ""./dist"",
    ""rootDir"": "".""
  }
}");

        // Write package.json to force ESM (enables top-level await)
        var packageJsonPath = Path.Combine(_workspaceDir, "package.json");
        await File.WriteAllTextAsync(packageJsonPath, @"{
  ""type"": ""module"",
  ""description"": ""Auto-generated Code Mode workspace"",
  ""license"": ""MIT""
}");

        // Generate tool files for each connected server
        var serversDir = Path.Combine(_workspaceDir, "servers");
        Directory.CreateDirectory(serversDir);

        var allTools = await _mcp.GetAllToolsAsync();
        var toolsByServer = allTools.GroupBy(t => t.ServerName);

        foreach (var serverGroup in toolsByServer)
        {
            var serverName = SanitizeName(serverGroup.Key);
            var serverDir = Path.Combine(serversDir, serverName);
            Directory.CreateDirectory(serverDir);

            var toolExports = new List<string>();

            foreach (var toolWithServer in serverGroup)
            {
                var tool = toolWithServer.Tool;
                if (string.IsNullOrEmpty(tool.Name)) continue;

                var toolFileName = SanitizeToolName(tool.Name);
                var toolFilePath = Path.Combine(serverDir, $"{toolFileName}.ts");
                var toolContent = GenerateToolFile(serverName, tool);
                await File.WriteAllTextAsync(toolFilePath, toolContent);

                toolExports.Add(toolFileName);
                _logger.LogDebug("Generated tool file: servers/{Server}/{Tool}.ts", serverName, toolFileName);
            }

            // Generate index.ts that re-exports all tools
            var indexContent = new StringBuilder();
            foreach (var export in toolExports)
            {
                indexContent.AppendLine($"export {{ {export} }} from './{export}';");
            }
            await File.WriteAllTextAsync(Path.Combine(serverDir, "index.ts"), indexContent.ToString());
        }

        _logger.LogInformation("Generated tool files for {ServerCount} servers, {ToolCount} tools",
            toolsByServer.Count(), allTools.Count);

        return _workspaceDir;
    }

    /// <summary>
    /// Returns a summary of available servers and their tool files for system prompt inclusion.
    /// This is the "filesystem" the LLM will navigate.
    /// </summary>
    public async Task<string> GetToolTreeSummaryAsync()
    {
        if (_workspaceDir == null)
        {
            await GenerateToolFilesAsync();
        }

        var sb = new StringBuilder();
        var serversDir = Path.Combine(_workspaceDir!, "servers");

        if (!Directory.Exists(serversDir)) return "No servers available.";

        foreach (var serverDir in Directory.GetDirectories(serversDir))
        {
            var serverName = Path.GetFileName(serverDir);
            sb.AppendLine($"  {serverName}/");

            foreach (var toolFile in Directory.GetFiles(serverDir, "*.ts"))
            {
                var fileName = Path.GetFileName(toolFile);
                if (fileName == "index.ts") continue;
                sb.AppendLine($"    {fileName}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Searches available tools by keyword. Returns matching tool file contents.
    /// Used by the search_tools function the LLM can call.
    /// </summary>
    public async Task<string> SearchToolsAsync(string query, string detailLevel = "full")
    {
        var allTools = await _mcp.GetAllToolsAsync();
        var queryLower = query.ToLowerInvariant();

        var matches = allTools.Where(t =>
            (t.Tool.Name?.ToLowerInvariant().Contains(queryLower) ?? false) ||
            (t.Tool.Description?.ToLowerInvariant().Contains(queryLower) ?? false) ||
            t.ServerName.ToLowerInvariant().Contains(queryLower)
        ).ToList();

        if (matches.Count == 0) return "No tools found matching the query.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {matches.Count} matching tool(s):");
        sb.AppendLine();

        foreach (var match in matches)
        {
            switch (detailLevel.ToLowerInvariant())
            {
                case "name":
                    sb.AppendLine($"- {match.ServerName}/{match.Tool.Name}");
                    break;
                case "description":
                    sb.AppendLine($"- {match.ServerName}/{match.Tool.Name}: {match.Tool.Description ?? "No description"}");
                    break;
                default: // "full"
                    var serverName = SanitizeName(match.ServerName);
                    var toolFileName = SanitizeToolName(match.Tool.Name);
                    sb.AppendLine($"### {match.ServerName}/{match.Tool.Name}");
                    sb.AppendLine($"Description: {match.Tool.Description ?? "No description"}");
                    sb.AppendLine($"Import: `import {{ {toolFileName} }} from './servers/{serverName}';`");
                    if (match.Tool.InputSchema.HasValue)
                    {
                        sb.AppendLine($"Input Schema: ```json\n{match.Tool.InputSchema.Value.GetRawText()}\n```");
                    }
                    sb.AppendLine();
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Executes LLM-generated TypeScript code in a sandboxed Node.js process.
    /// The code can import MCP tool wrappers from ./servers/ directory.
    /// Tool calls are bridged back to .NET via a localhost HTTP server.
    /// Returns stdout output (what the LLM should see) and any errors.
    /// </summary>
    public async Task<CodeExecutionResult> ExecuteCodeAsync(string code, CancellationToken ct = default)
    {
        if (_workspaceDir == null)
        {
            await GenerateToolFilesAsync();
        }

        // Write the user code to a temp file
        var codeFilePath = Path.Combine(_workspaceDir!, $"__exec_{Guid.NewGuid():N}.ts");
        await File.WriteAllTextAsync(codeFilePath, code, ct);

        _logger.LogInformation("Executing code in sandbox: {Path}", codeFilePath);
        _logger.LogDebug("Code:\n{Code}", code);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        // Start the HTTP bridge server
        HttpListener? httpListener = null;
        int bridgePort = 0;
        CancellationTokenSource? serverCts = null;

        try
        {
            // Find a free port and start the bridge HTTP server
            bridgePort = GetFreePort();
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://localhost:{bridgePort}/");
            httpListener.Start();
            serverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = RunBridgeServerAsync(httpListener, serverCts.Token);
            _logger.LogInformation("Bridge HTTP server started on port {Port}", bridgePort);

            var startInfo = new ProcessStartInfo
            {
                FileName = GetExecutableName("npx"),
                Arguments = GetNpxArguments($"tsx \"{codeFilePath}\""),
                WorkingDirectory = _workspaceDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Set NODE_PATH so imports resolve, and pass bridge port
            startInfo.EnvironmentVariables["NODE_PATH"] = _workspaceDir;
            startInfo.EnvironmentVariables["MCP_BRIDGE_PORT"] = bridgePort.ToString();

            using var process = new Process { StartInfo = startInfo };
            var outputDone = new TaskCompletionSource<bool>();
            var errorDone = new TaskCompletionSource<bool>();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    outputDone.TrySetResult(true);
                    return;
                }
                stdout.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    errorDone.TrySetResult(true);
                    return;
                }
                // Filter out noisy tsx/node warnings
                if (!e.Data.Contains("ExperimentalWarning") && !e.Data.Contains("--experimental"))
                {
                    stderr.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for process completion with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
                // Give a small window for remaining output
                await Task.WhenAny(outputDone.Task, Task.Delay(2000, CancellationToken.None));
                await Task.WhenAny(errorDone.Task, Task.Delay(1000, CancellationToken.None));
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                if (ct.IsCancellationRequested)
                {
                    return new CodeExecutionResult
                    {
                        Output = stdout.ToString(),
                        Error = "Execution cancelled by user.",
                        ExitCode = -1,
                        TimedOut = false
                    };
                }

                return new CodeExecutionResult
                {
                    Output = stdout.ToString(),
                    Error = $"Execution timed out after {_timeoutSeconds} seconds.",
                    ExitCode = -1,
                    TimedOut = true
                };
            }

            return new CodeExecutionResult
            {
                Output = stdout.ToString().TrimEnd(),
                Error = stderr.ToString().TrimEnd(),
                ExitCode = process.ExitCode,
                TimedOut = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute code");
            return new CodeExecutionResult
            {
                Output = "",
                Error = $"Failed to start code execution: {ex.Message}",
                ExitCode = -1,
                TimedOut = false
            };
        }
        finally
        {
            // Clean up HTTP server and temp code file
            try { serverCts?.Cancel(); } catch { }
            try { httpListener?.Stop(); httpListener?.Close(); } catch { }
            try { File.Delete(codeFilePath); } catch { }
        }
    }

    /// <summary>
    /// Runs the HTTP bridge server that handles tool call requests from the sandbox.
    /// Listens for POST /call-tool requests and routes them to MCP.
    /// </summary>
    private async Task RunBridgeServerAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await listener.GetContextAsync().WaitAsync(ct);
                _ = HandleHttpBridgeRequestAsync(context);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bridge server error");
            }
        }
    }

    /// <summary>
    /// Handles a single HTTP bridge request from the sandboxed process.
    /// Expects POST with JSON body { tool: string, args: object }.
    /// Returns JSON response { result: ... } or { error: string }.
    /// </summary>
    private async Task HandleHttpBridgeRequestAsync(HttpListenerContext context)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        try
        {
            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            var request = JsonDocument.Parse(body);
            var root = request.RootElement;

            var toolName = root.GetProperty("tool").GetString()!;
            var args = root.GetProperty("args");

            _logger.LogInformation("HTTP bridge call from sandbox: {ToolName}", toolName);

            try
            {
                var result = await _mcp.CallToolByNameAsync(toolName, args);
                var responseJson = JsonSerializer.Serialize(new { result });
                var bytes = Encoding.UTF8.GetBytes(responseJson);
                response.StatusCode = 200;
                await response.OutputStream.WriteAsync(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bridge tool call failed: {ToolName}", toolName);
                var errorJson = JsonSerializer.Serialize(new { error = ex.Message });
                var bytes = Encoding.UTF8.GetBytes(errorJson);
                response.StatusCode = 200; // Still 200, error in body
                await response.OutputStream.WriteAsync(bytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse HTTP bridge request");
            var errorJson = JsonSerializer.Serialize(new { error = "Invalid request: " + ex.Message });
            var bytes = Encoding.UTF8.GetBytes(errorJson);
            response.StatusCode = 400;
            await response.OutputStream.WriteAsync(bytes);
        }
        finally
        {
            response.Close();
        }
    }

    /// <summary>
    /// Finds a free TCP port to use for the bridge HTTP server.
    /// </summary>
    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Checks if Node.js is available on the system.
    /// </summary>
    public async Task<bool> IsNodeAvailableAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = GetExecutableName("node"),
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            var version = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            _logger.LogInformation("Node.js version: {Version}", version.Trim());
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Cleans up the workspace directory.
    /// </summary>
    public void Cleanup()
    {
        if (_workspaceDir != null && Directory.Exists(_workspaceDir))
        {
            try
            {
                Directory.Delete(_workspaceDir, recursive: true);
                _logger.LogInformation("Cleaned up workspace: {Dir}", _workspaceDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup workspace: {Dir}", _workspaceDir);
            }
            _workspaceDir = null;
        }
    }

    #region Code Generation

    /// <summary>
    /// Generates the bridge module (__mcp_bridge.ts) that tool wrappers import from.
    /// Uses HTTP to communicate with the .NET host bridge server.
    /// </summary>
    private string GenerateBridgeModule()
    {
        return @"// Auto-generated MCP Bridge Module
// This module communicates with the .NET host via HTTP.
// Tool wrappers import callMCPTool from this module.

const BRIDGE_PORT = process.env.MCP_BRIDGE_PORT;
if (!BRIDGE_PORT) {
    throw new Error('MCP_BRIDGE_PORT environment variable is not set.');
}
const BRIDGE_URL = `http://localhost:${BRIDGE_PORT}/call-tool`;

/**
 * Calls an MCP tool via the .NET host HTTP bridge.
 * @param toolName - The full MCP tool name
 * @param args - The arguments to pass to the tool
 * @returns The tool result
 */
export async function callMCPTool<T = any>(toolName: string, args: any): Promise<T> {
    const response = await fetch(BRIDGE_URL, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tool: toolName, args })
    });
    
    const data = await response.json() as any;
    
    if (data.error) {
        throw new Error(data.error);
    }
    
    return data.result as T;
}

/**
 * Extracts text content from an MCP tool result.
 */
export function extractText(result: any): string {
    if (result?.content && Array.isArray(result.content)) {
        return result.content
            .filter((item: any) => item.type === 'text')
            .map((item: any) => item.text)
            .join('\n');
    }
    return JSON.stringify(result);
}
";
    }

    /// <summary>
    /// Generates a TypeScript wrapper file for a single MCP tool.
    /// </summary>
    private string GenerateToolFile(string serverName, McpTool tool)
    {
        var functionName = SanitizeToolName(tool.Name);
        var description = SanitizeDescription(tool.Description) ?? "No description available";

        // Generate TypeScript interface from input schema
        var inputInterface = GenerateInputInterface(tool);
        var inputTypeName = $"{Capitalize(functionName)}Input";

        var sb = new StringBuilder();
        sb.AppendLine($"// Auto-generated tool wrapper for: {tool.Name}");
        sb.AppendLine($"// Server: {serverName}");
        sb.AppendLine($"import {{ callMCPTool, extractText }} from '../../__mcp_bridge';");
        sb.AppendLine();
        sb.AppendLine(inputInterface);
        sb.AppendLine();
        sb.AppendLine($"/**");
        sb.AppendLine($" * {description}");
        sb.AppendLine($" */");
        sb.AppendLine($"export async function {functionName}(input: {inputTypeName}): Promise<any> {{");
        sb.AppendLine($"    return callMCPTool('{tool.Name}', input);");
        sb.AppendLine($"}}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a TypeScript interface from an MCP tool's input schema.
    /// </summary>
    private string GenerateInputInterface(McpTool tool)
    {
        var functionName = SanitizeToolName(tool.Name);
        var typeName = $"{Capitalize(functionName)}Input";
        var sb = new StringBuilder();
        sb.AppendLine($"export interface {typeName} {{");

        if (tool.InputSchema.HasValue)
        {
            var schema = tool.InputSchema.Value;
            var requiredFields = new HashSet<string>();

            if (schema.TryGetProperty("required", out var requiredArray))
            {
                foreach (var req in requiredArray.EnumerateArray())
                {
                    requiredFields.Add(req.GetString() ?? "");
                }
            }

            if (schema.TryGetProperty("properties", out var properties))
            {
                foreach (var prop in properties.EnumerateObject())
                {
                    var tsType = JsonSchemaToTypeScript(prop.Value);
                    var optional = requiredFields.Contains(prop.Name) ? "" : "?";
                    var propDescription = "";
                    if (prop.Value.TryGetProperty("description", out var desc))
                    {
                        propDescription = $" // {SanitizeDescription(desc.GetString())}";
                    }
                    sb.AppendLine($"    {prop.Name}{optional}: {tsType};{propDescription}");
                }
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Converts JSON Schema type to TypeScript type.
    /// </summary>
    private string JsonSchemaToTypeScript(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var typeProp))
        {
            return "any";
        }

        return typeProp.GetString() switch
        {
            "string" => "string",
            "number" => "number",
            "integer" => "number",
            "boolean" => "boolean",
            "array" => schema.TryGetProperty("items", out var items) 
                ? $"{JsonSchemaToTypeScript(items)}[]" 
                : "any[]",
            "object" => "Record<string, any>",
            _ => "any"
        };
    }

    /// <summary>
    /// Sanitizes a server name for use as a directory name.
    /// </summary>
    private static string SanitizeName(string name)
    {
        // Replace invalid chars with hyphens, lowercase
        var result = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                result.Append(c);
            }
            else if (c == ' ' || c == '.')
            {
                result.Append('-');
            }
        }
        return result.Length > 0 ? result.ToString() : "unknown";
    }

    /// <summary>
    /// Sanitizes a tool name for use as a valid TypeScript function/file name.
    /// MCP tool names often use patterns like "server__tool_name" or "server/tool_name".
    /// </summary>
    private static string SanitizeToolName(string name)
    {
        // Remove common prefixes (server__) and convert to camelCase-friendly name
        var result = new StringBuilder();
        bool capitalizeNext = false;

        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                result.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = result.Length > 0; // Don't capitalize the first char
            }
        }

        var str = result.ToString();
        // Ensure it starts with a letter
        if (str.Length > 0 && char.IsDigit(str[0]))
        {
            str = "_" + str;
        }
        return str.Length > 0 ? str : "unknownTool";
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// Sanitizes tool descriptions to remove problematic Unicode characters.
    /// Strips all non-ASCII characters to prevent encoding issues in TypeScript files.
    /// </summary>
    private static string SanitizeDescription(string? description)
    {
        if (string.IsNullOrEmpty(description)) return "";
        
        // First, try to replace known Unicode characters with ASCII equivalents
        var result = description
            .Replace("*/", "* /") // Escape comment terminators
            .Replace("\u2022", "*") // Bullet points
            .Replace("\u2500", "-") // Box-drawing horizontal
            .Replace("\u250C", "+") // Box-drawing top-left
            .Replace("\u2514", "+") // Box-drawing bottom-left
            .Replace("\u251C", "+") // Box-drawing vertical-right
            .Replace("\u2510", "+") // Box-drawing top-right
            .Replace("\u2518", "+") // Box-drawing bottom-right
            .Replace("\u252C", "+") // Box-drawing horizontal-down
            .Replace("\u2524", "+") // Box-drawing vertical-left
            .Replace("\u2534", "+") // Box-drawing horizontal-up
            .Replace("\u253C", "+") // Box-drawing cross
            .Replace("\u2502", "|") // Box-drawing vertical
            .Replace("\u2192", "->") // Arrow right
            .Replace("\u2190", "<-") // Arrow left
            .Replace("\u2191", "^") // Arrow up
            .Replace("\u2193", "v") // Arrow down
            .Replace("\u201C", "\"") // Left double quote
            .Replace("\u201D", "\"") // Right double quote
            .Replace("\u2018", "'") // Left single quote
            .Replace("\u2019", "'") // Right single quote
            .Replace("\u2026", "...") // Ellipsis
            .Replace("\u2013", "-") // En dash
            .Replace("\u2014", "--"); // Em dash
        
        // Then, strip any remaining non-ASCII characters (>127)
        // This handles malformed UTF-8 sequences and other problematic chars
        var sb = new StringBuilder();
        foreach (var c in result)
        {
            if (c < 128) // Keep only ASCII characters
            {
                sb.Append(c);
            }
            else
            {
                // Replace any other Unicode char with a space
                sb.Append(' ');
            }
        }
        
        // Clean up multiple spaces
        result = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ");
        
        return result;
    }


    /// <summary>
    /// Resolves the correct executable name for the current platform.
    /// On Windows, we use cmd.exe to run npx so that PATH and npm internals resolve correctly.
    /// </summary>
    private static string GetExecutableName(string baseName)
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            if (baseName.Equals("npx", StringComparison.OrdinalIgnoreCase))
                return "cmd.exe";
            if (baseName.Equals("node", StringComparison.OrdinalIgnoreCase))
                return "node.exe";
            return baseName + ".exe";
        }
        return baseName;
    }

    /// <summary>
    /// Wraps arguments for npx commands. On Windows, we run through cmd.exe /c
    /// to ensure proper PATH and npm module resolution.
    /// </summary>
    private static string GetNpxArguments(string args)
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            return $"/c npx {args}";
        }
        return args;
    }

    #endregion
}

/// <summary>
/// Result of executing code in the sandboxed environment.
/// </summary>
public class CodeExecutionResult
{
    /// <summary>
    /// Standard output from the code execution (what gets returned to the LLM).
    /// </summary>
    public string Output { get; set; } = "";

    /// <summary>
    /// Standard error output (syntax errors, runtime errors, etc.)
    /// </summary>
    public string Error { get; set; } = "";

    /// <summary>
    /// Process exit code. 0 = success.
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Whether the execution was killed due to timeout.
    /// </summary>
    public bool TimedOut { get; set; }

    /// <summary>
    /// Whether the execution completed successfully.
    /// </summary>
    public bool Success => ExitCode == 0 && !TimedOut;

    /// <summary>
    /// Returns a formatted result string suitable for returning to the LLM.
    /// </summary>
    public string ToResultString()
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(Output))
        {
            sb.AppendLine(Output);
        }

        if (!string.IsNullOrEmpty(Error))
        {
            sb.AppendLine($"\n[stderr]\n{Error}");
        }

        if (TimedOut)
        {
            sb.AppendLine("\n[TIMED OUT]");
        }
        else if (ExitCode != 0)
        {
            sb.AppendLine($"\n[Exit code: {ExitCode}]");
        }

        return sb.ToString().TrimEnd();
    }
}
