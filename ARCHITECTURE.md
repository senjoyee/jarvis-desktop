# Jarvis Desktop Architecture

This document outlines the high-level architecture of the Jarvis Desktop application.

## Architectural Overview

The application is a **Windows Desktop Host** built with .NET 8 and WPF, which embeds a **WebView2** control to host a modern **React** user interface.

```mermaid
graph TD
    subgraph Host [Jarvis Desktop Host .NET 8 WPF]
        style Host fill:#ffffff,stroke:#2c3e50,stroke-width:2px,color:#2c3e50
        
        subgraph UI_Container [WebView2 Container]
            style UI_Container fill:#f8f9fa,stroke:#bdc3c7,stroke-dasharray: 5 5,color:#2c3e50
            ReactApp[React UI<br/>(TypeScript + Vite + Fluent UI)]
            style ReactApp fill:#e8f8f5,stroke:#1abc9c,stroke-width:2px,color:#2c3e50
        end

        Bridge[Inter-Process Communication Bridge<br/>(RPC API)]
        style Bridge fill:#fef9e7,stroke:#f1c40f,stroke-width:2px,color:#2c3e50

        subgraph Backend [Backend Services Layer]
            style Backend fill:#f4f6f7,stroke:#bdc3c7,color:#2c3e50
            ChatSvc[Chat Service<br/>(Streaming)]
            style ChatSvc fill:#ebf5fb,stroke:#3498db,stroke-width:2px,color:#2c3e50
            
            McpMgr[MCP Manager<br/>(Client & Registry)]
            style McpMgr fill:#ebf5fb,stroke:#3498db,stroke-width:2px,color:#2c3e50
            
            ConvSvc[Conversation Service<br/>(History Logic)]
            style ConvSvc fill:#ebf5fb,stroke:#3498db,stroke-width:2px,color:#2c3e50
            
            SecSvc[Secrets Service<br/>(Security)]
            style SecSvc fill:#ebf5fb,stroke:#3498db,stroke-width:2px,color:#2c3e50
        end
    end

    %% Connections within Host
    ReactApp <-->|JSON-RPC| Bridge
    Bridge <--> ChatSvc
    Bridge <--> McpMgr
    Bridge <--> ConvSvc
    Bridge <--> SecSvc

    %% External Connections
    ChatSvc <-->|HTTPS| OpenRouter[OpenRouter API<br/>(Multi-Provider Gateway)]
    style OpenRouter fill:#ffffff,stroke:#2c3e50,stroke-width:2px,color:#2c3e50

    OpenRouter <--> Providers[AI Providers<br/>(OpenAI, Anthropic, Google, Moonshot, etc.)]
    style Providers fill:#fadbd8,stroke:#e74c3c,stroke-width:2px,color:#2c3e50

    McpMgr <-->|Stdio / SSE| ExtMCP[External MCP Servers<br/>(Local & Remote)]
    style ExtMCP fill:#fadbd8,stroke:#e74c3c,stroke-width:2px,color:#2c3e50

    ConvSvc <-->|SQL| SQLite[(SQLite Database<br/>%LOCALAPPDATA%)]
    style SQLite fill:#eaeded,stroke:#95a5a6,stroke-width:2px,color:#2c3e50

    SecSvc <-->|Win32 API| CredMan[Windows Credential Manager]
    style CredMan fill:#eaeded,stroke:#95a5a6,stroke-width:2px,color:#2c3e50
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
*   **ChatService**: Manages connections to OpenRouter, handling request composition and response streaming. Supports models from OpenAI, Anthropic, Google, Moonshot (Kimi), DeepSeek, Meta, Mistral, xAI, and Qwen.
*   **McpManager**: A robust client for the Model Context Protocol. It spawns local processes (stdio) or connects to remote streams (SSE), managing tool discovery and execution.
*   **ConversationService**: Handles business logic for chat history, ensuring messages are saved and retrieved correctly.
*   **SecretsService**: Provides a secure interface to the Windows Credential Manager for storing API keys.

### 4. Persistence & Security
*   **SQLite**: A local `jarvis.db` file stores conversation history and non-sensitive configuration.
*   **Windows Credential Manager**: Securely stores sensitive data like OpenRouter API keys and MCP authentication tokens.
