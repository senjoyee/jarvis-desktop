# Jarvis Desktop

A Windows desktop chat client with MCP (Model Context Protocol) support, built with WPF + WebView2 hosting a React UI.

## Features

- **Chat with OpenAI models** - Stream responses with per-message model selection
- **Conversation management** - Create, rename, delete conversations with SQLite persistence
- **MCP Server support** - Add local (stdio) and remote (SSE) MCP servers
- **Tool management** - List tools, manually invoke tools for testing
- **Secure credential storage** - API keys stored in Windows Credential Manager

## Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/)
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (usually pre-installed on Windows 10/11)

## Development Setup

### 1. Install UI dependencies

```bash
cd ui
npm install
```

### 2. Start the UI dev server

```bash
cd ui
npm run dev
```

This starts Vite on http://localhost:5173

### 3. Run the WPF application

In a separate terminal:

```bash
cd src/ChloyeDesktop
dotnet run
```

Or open `ChloyeDesktop.sln` in Visual Studio and press F5.

The app will automatically connect to the Vite dev server when running in debug mode.

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

#### Local Server (stdio)
1. Go to MCP Servers
2. Click "Add Server"
3. Select "Local (stdio)"
4. Enter command (e.g., `node`, `python`, `npx`)
5. Enter arguments
6. Click "Add Server" then "Start"

#### Remote Server (SSE)
1. Go to MCP Servers
2. Click "Add Server"
3. Select "Remote (SSE)"
4. Enter server URL
5. Configure authentication if needed
6. Click "Add Server" then "Connect"

## Data Storage

- **Database**: `%LOCALAPPDATA%\ChloyeDesktop\chloye.db`
- **Credentials**: Windows Credential Manager
- **WebView2 data**: `%LOCALAPPDATA%\ChloyeDesktop\WebView2`

## Building for Production

### Build UI

```bash
cd ui
npm run build
```

This outputs to `src/ChloyeDesktop/wwwroot/`

### Build WPF Application

```bash
dotnet publish src/ChloyeDesktop -c Release -r win-x64 --self-contained
```

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
