# Windows Desktop Chat + MCP Client (Jarvis)

Build a Windows-first desktop chat client (Claude/ChatGPT-style) using a WPF shell hosting a React UI via WebView2, with streaming OpenAI chat (GPT-5.2), per-message model selection, conversation history, and the ability to add/manage local+remote MCP servers (connect/list tools/manual tool test).

## Objective
- Deliver a local Windows desktop app that:
  - Supports a chat experience with per-message model selection (initially OpenAI `gpt-5.2` only).
  - Streams assistant responses.
  - Persists conversations locally.
  - Lets users add MCP servers (local stdio and remote SSE-first), connect, list tools, and manually invoke a tool for testing.

## Acceptance Criteria (MVP)
- App starts on Windows in dev mode without installer (developer workflow documented).
- Chat:
  - Can create/select conversations.
  - Per-message model selector exists (even if only `gpt-5.2` is available initially).
  - Streaming responses render incrementally.
  - Markdown + code blocks render correctly.
- Secrets:
  - OpenAI API key stored in Windows Credential Manager (no plaintext key persisted).
- Persistence:
  - Conversations stored locally (SQLite).
  - MCP server configurations stored locally (non-secret fields only).
- MCP:
  - User can add a local server (command + args + env) and start/stop it.
  - User can add a remote server (URL; SSE-first) and connect/disconnect.
  - User can view connection status and logs.
  - User can list tools for a connected server.
  - User can manually call a tool (provide name + JSON args) and view result.

## Non-Goals (for MVP)
- No attachments / image upload.
- No local models (no Ollama).
- No automated “model calls tools” (tool-calling orchestration) beyond manual tool test.
- No installer/signing; packaging is a later milestone.

## Constraints / Assumptions
- Target OS: Windows only (for now), but keep code structure compatible with future expansion.
- Corporate constraints: avoid Electron distribution; prefer Windows-native shell and system WebView2.
- Remote MCP transport: implement SSE-first; keep transport abstraction extensible.
- Do not store API keys in config files; use Windows Credential Manager.

## Proposed Tech Stack
- Shell: .NET 8 + **WPF**
- Embedded web UI: **WebView2**
- Frontend: **React + TypeScript** (Vite)
- Styling/components: Fluent UI (recommended) or Tailwind (optional)
- Persistence: **SQLite** (via `Microsoft.Data.Sqlite` or EF Core)
- HTTP: `HttpClient`
- Logging: `Microsoft.Extensions.Logging` + rolling file sink (or simple file logger)

## High-Level Architecture
- **WPF Host (Desktop Shell)**
  - Owns windowing, menu/tray later, native dialogs, app lifecycle.
  - Embeds WebView2 pointing to:
    - Dev: `http://localhost:<vitePort>`
    - Prod: packaged static build served from local file / embedded resource.
  - Provides a native “backend bridge” for:
    - OpenAI chat streaming
    - MCP server management
    - persistence
    - secret storage

- **React UI (Renderer)**
  - Renders conversations/chat.
  - Provides screens for Settings and MCP Servers.
  - Talks to backend via WebView2 messaging.

- **Backend Services (in-proc .NET layer)**
  - `ChatService` (OpenAI streaming)
  - `ConversationStore` (SQLite)
  - `SecretsService` (Credential Manager)
  - `McpManager` (registry, connect/disconnect, process management, tool listing/calls)

## UI/UX (MVP screens)
- **Left sidebar**
  - Conversations list (create/rename/delete).
  - MCP Servers section (list + status).
- **Main chat**
  - Message list (markdown rendering).
  - Composer:
    - Text input
    - **Model selector per message**
    - Send/Stop streaming
- **MCP Servers screen/panel**
  - Add server modal:
    - Local: name, command, args, working dir (optional), env (key/value), auto-start toggle
    - Remote: name, URL, auth (none/bearer), optional headers
  - Per-server details:
    - Status (connected/running/stopped/error)
    - Logs (last N lines)
    - Tools list
    - Tool test runner (tool name + JSON args + result)
- **Settings screen**
  - OpenAI API key set/clear/test
  - Data location (default local app data)

## Data Model (SQLite)
- `conversations`
  - `id` (GUID), `title`, `created_at`, `updated_at`
- `messages`
  - `id` (GUID), `conversation_id`, `role` (user/assistant/system), `content`, `created_at`
  - `model` (string) stored per message
  - optionally: `metadata_json` (for future tool calls)
- `mcp_servers` (non-secret config)
  - `id` (GUID), `name`, `type` (local/remote)
  - local fields: `command`, `args_json`, `cwd`, `env_json`, `auto_start`
  - remote fields: `url`, `auth_type`, `auth_ref` (reference key name in credential manager)
  - `created_at`, `updated_at`

## Secrets Storage
- Use **Windows Credential Manager** entries keyed by app + purpose:
  - `ChloyeDesktop/OpenAI` -> API key
  - `ChloyeDesktop/MCP/<serverId>` -> bearer token (if any)
- UI should support set/clear and “test connectivity” (OpenAI only for MVP).

## Backend↔UI Communication (WebView2)
- Prefer message-based RPC:
  - UI -> backend: `window.chrome.webview.postMessage({ id, method, params })`
  - backend -> UI: `postMessage({ id, result|error })`
- Define stable API surface (versioned):
  - `conversations.list/create/delete/rename`
  - `messages.list/send`
  - `settings.get/setApiKey/clearApiKey/testOpenAI`
  - `mcp.list/add/update/remove/start/stop/connect/disconnect/logs`
  - `mcp.tools.list`
  - `mcp.tools.call`

## OpenAI Chat (MVP)
- Provider abstraction even if only OpenAI is implemented initially:
  - `IChatProvider.StreamCompletion(request)`
- Requirements:
  - Streaming tokens to UI (incremental updates by message id)
  - Cancel/Stop button wired to cancellation token
  - Per-message model field in request (defaults to `gpt-5.2`)

## MCP Support (MVP)
- Implement an MCP client abstraction:
  - `IMcpTransport` (stdio, sse)
  - `McpClient` (JSON-RPC 2 request/response correlation, initialize, tools/list, tools/call)
- Local MCP (stdio):
  - Spawn process with command+args
  - Wire stdin/stdout
  - Parse JSON-RPC messages (line-delimited or framed per MCP transport requirements)
  - Capture stderr for logs
  - Support start/stop/restart and exit codes
- Remote MCP (SSE-first):
  - Connect to event stream
  - Send requests via HTTP endpoint as per MCP SSE transport spec
  - Auth via bearer token stored in credential manager
- MVP capabilities:
  - `initialize`
  - `tools/list`
  - `tools/call`
  - Basic error handling and reconnect UX

## Testing Strategy
- Unit tests (.NET):
  - JSON-RPC correlation and transport parsing
  - SQLite persistence
  - Secrets service (mocked wrapper)
- Frontend tests:
  - Component-level tests for message rendering and MCP screens (lightweight)
- Manual test checklist:
  - Set OpenAI key; stream a response; stop mid-stream; resume new message
  - Create/select conversation; restart app; ensure history persists
  - Add local MCP server; start; list tools; call tool
  - Add remote MCP server; connect; list tools; call tool

## Milestones / Phasing
1) Skeleton + Dev Workflow
- Create .NET WPF host + WebView2.
- Create React UI with basic routing/layout.
- Wire message-based RPC bridge.

2) Chat MVP
- OpenAI key management (Credential Manager).
- Conversation + message persistence (SQLite).
- Streaming chat with per-message model selector.

3) MCP Manager MVP
- MCP server registry + persistence.
- Local stdio server start/stop + logs.
- Remote SSE connect + logs.
- Tools list + manual tool runner.

4) Hardening
- Better error states and retries.
- Telemetry/log file export (optional).
- Prepare for Phase 2: tool-calling orchestration.

## Risks / Mitigations
- WebView2 runtime availability:
  - Detect missing runtime and guide install (or bundle later).
- MCP SSE transport specifics may vary:
  - Keep transport pluggable; validate against at least one known-good MCP server.
- Corporate security policies:
  - Keep binaries signed later; minimize suspicious behaviors; provide clear UI confirmations when spawning local servers.

## Phase 2 (Post-MVP)
- Tool-calling orchestration: model can call MCP tools automatically.
- Provider expansion: Azure OpenAI, Anthropic.
- Packaging: installer, signing, auto-update.
