# Jarvis Desktop

A Windows desktop chat client with MCP (Model Context Protocol) support, built with WPF + WebView2 hosting a React UI.

## Features

- **Chat with OpenAI models** - Stream responses with per-message model selection
- **Conversation management** - Create, rename, delete conversations with SQLite persistence
- **MCP Server support** - Add local (stdio), remote (SSE), and HTTP MCP servers
- **Tool management** - List tools, manually invoke tools for testing
- **Secure credential storage** - API keys stored in Windows Credential Manager

## 🚀 Quick Start

### Prerequisites

| Requirement | Version | Download |
|-------------|---------|----------|
| Windows | 10/11 | - |
| .NET 8 SDK | 8.0+ | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Node.js | 18+ | [Download](https://nodejs.org/) |
| WebView2 Runtime | Latest | Usually pre-installed on Windows 10/11 |

### Run the Application

```powershell
# 1. Clone the repository
git clone https://github.com/yourname/Chloye_desktop.git
cd Chloye_desktop

# 2. Build the React UI
cd ui
npm install
npm run build

# 3. Run the application
cd ../src/ChloyeDesktop
dotnet run
```

The app should launch and you can configure your OpenAI API key in Settings.

## Development Setup

For active development with hot-reload on the UI:

```powershell
# Terminal 1: Start the React dev server
cd ui
npm run dev
# → Runs on http://localhost:5173

# Terminal 2: Run the WPF app in dev mode
$env:CHLOYE_DEV_MODE="1"
cd src/ChloyeDesktop
dotnet run
```

Or open `ChloyeDesktop.sln` in Visual Studio and press F5.

## Project Structure

```
├── src/
│   └── ChloyeDesktop/          # WPF application
│       ├── Services/           # Backend services
│       │   ├── ChatService.cs      # OpenAI streaming
│       │   ├── ConversationService.cs
│       │   ├── DatabaseService.cs  # SQLite persistence
│       │   ├── McpManager.cs       # MCP client management
│       │   └── SecretsService.cs   # Credential Manager
│       ├── Bridge/             # WebView2 RPC bridge
│       ├── Models/             # Data models
│       └── MainWindow.xaml     # WPF host window
├── ui/                         # React frontend
│   └── src/
│       ├── components/         # React components
│       ├── pages/              # Page components
│       ├── services/           # Bridge service
│       ├── store.ts            # Zustand state
│       └── types/              # TypeScript types
└── ChloyeDesktop.sln
```

## Configuration

### OpenAI API Key

1. Launch the app
2. Go to Settings
3. Enter your OpenAI API key
4. Click "Test Connection" to verify

The key is stored in Windows Credential Manager under `ChloyeDesktop/OpenAI`.

### MCP Servers

MCP servers are configured via the config file at:
`%LOCALAPPDATA%\ChloyeDesktop\mcp_config.json`

Example configuration:
```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:/path/to/folder"]
    },
    "remote-server": {
      "type": "http",
      "url": "https://your-mcp-server.com/mcp"
    }
  }
}
```

## Data Storage

| Data | Location |
|------|----------|
| Database | `%LOCALAPPDATA%\ChloyeDesktop\jarvis.db` |
| MCP Config | `%LOCALAPPDATA%\ChloyeDesktop\mcp_config.json` |
| Credentials | Windows Credential Manager |
| WebView2 Cache | `%LOCALAPPDATA%\ChloyeDesktop\WebView2` |

## 📦 Building for Production

### Build a Distributable Executable

```powershell
# 1. Build the UI
cd ui
npm run build

# 2. Publish as self-contained single file
cd ../src/ChloyeDesktop
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

The executable will be at:
`src/ChloyeDesktop/bin/Release/net8.0-windows/win-x64/publish/ChloyeDesktop.exe`

This creates a **single ~80-100MB executable** that includes the .NET runtime and all dependencies. Users don't need to install anything.

### Build Options

| Option | Command Flag | Result |
|--------|--------------|--------|
| Self-contained | `--self-contained true` | Includes .NET runtime |
| Single file | `-p:PublishSingleFile=true` | One .exe file |
| Compressed | `-p:EnableCompressionInSingleFile=true` | Smaller file size |
| Framework-dependent | `--self-contained false` | Requires .NET 8 installed |

## Tech Stack

- **Shell**: .NET 8 + WPF
- **Embedded UI**: WebView2
- **Frontend**: React 18 + TypeScript + Vite
- **UI Components**: Fluent UI React
- **State Management**: Zustand
- **Persistence**: SQLite
- **Secrets**: Windows Credential Manager

## License

MIT

