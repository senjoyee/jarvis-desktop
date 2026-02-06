# Chloye Desktop Architecture

This document outlines the high-level architecture of the Chloye Desktop application.

## Architectural Overview

The application is a **Windows Desktop Host** built with .NET 8 and WPF, which embeds a **WebView2** control to host a modern **React** user interface.

```mermaid
graph TD
    subgraph Host [Windows Desktop Host .NET 8 WPF]
        style Host fill:#2d3436,stroke:#6c5ce7,stroke-width:4px,color:white
        
        subgraph UI_Container [WebView2 Container]
            style UI_Container fill:#00b894,stroke:none,color:black
            ReactApp[React UI<br/>(TypeScript + Vite + Fluent UI)]
        end

        Bridge[Inter-Process Communication Bridge<br/>(RPC API)]
        style Bridge fill:#fdcb6e,stroke:none,color:black

        subgraph Backend [Backend Services Layer]
            style Backend fill:#0984e3,stroke:none,color:white
            ChatSvc[Chat Service<br/>(Streaming)]
            McpMgr[MCP Manager<br/>(Client & Registry)]
            ConvSvc[Conversation Service<br/>(History Logic)]
            SecSvc[Secrets Service<br/>(Security)]
        end
    end

    %% Connections within Host
    ReactApp <-->|JSON-RPC| Bridge
    Bridge <--> ChatSvc
    Bridge <--> McpMgr
    Bridge <--> ConvSvc
    Bridge <--> SecSvc

    %% External Connections
    ChatSvc <-->|HTTPS| OpenAI[OpenAI API<br/>(LLM Provider)]
    style OpenAI fill:#000000,stroke:#white,stroke-width:2px,color:white

    McpMgr <-->|Stdio / SSE| ExtMCP[External MCP Servers<br/>(Local & Remote)]
    style ExtMCP fill:#d63031,stroke:none,color:white

    ConvSvc <-->|SQL| SQLite[(SQLite Database<br/>%LOCALAPPDATA%)]
    style SQLite fill:#636e72,stroke:none,color:white

    SecSvc <-->|Win32 API| CredMan[Windows Credential Manager]
    style CredMan fill:#636e72,stroke:none,color:white
```

## Component Details

### 1. User Interface (Frontend)
*   **Tech Stack**: React 18, TypeScript, Vite, Fluent UI React.
*   **Role**: Renders the chat interface, settings, and MCP management screens.
*   **State Management**: `zustand` is used for client-side state.
*   **Communication**: Sends JSON-RPC messages to the host via `window.chrome.webview.postMessage`.

### 2. Windows Host (Backend)
*   **Tech Stack**: .NET 8, WPF (Windows Presentation Foundation).
*   **Role**: Acts as the application shell, managing the window lifecycle and system integrations.
*   **Bridge**: A custom bridge handles message passing between the WebView2 JavaScript context and the .NET runtime.

### 3. Services Layer
*   **ChatService**: Manages connections to OpenAI, handling request composition and response streaming.
*   **McpManager**: A robust client for the Model Context Protocol. It spawns local processes (stdio) or connects to remote streams (SSE), managing tool discovery and execution.
*   **ConversationService**: Handles business logic for chat history, ensuring messages are saved and retrieved correctly.
*   **SecretsService**: Provides a secure interface to the Windows Credential Manager for storing API keys.

### 4. Persistence & Security
*   **SQLite**: A local `chloye.db` file stores conversation history and non-sensitive configuration.
*   **Windows Credential Manager**: Securely stores sensitive data like OpenAI API keys and MCP authentication tokens.
