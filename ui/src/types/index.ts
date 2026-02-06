export interface Conversation {
  id: string
  title: string
  createdAt: string
  updatedAt: string
}

export interface ToolCallDetail {
  toolName: string
  arguments: string
  result?: string
  success?: boolean
  status: 'calling' | 'done'
}

export interface TokenUsage {
  inputTokens: number
  outputTokens: number
  reasoningTokens: number
  totalTokens: number
}

export interface Message {
  id: string
  conversationId: string
  role: 'user' | 'assistant' | 'system'
  content: string
  model: string
  createdAt: string
  metadataJson?: string
  reasoning?: string
  toolCalls?: ToolCallDetail[]
  tokenUsage?: TokenUsage
}

export interface McpServer {
  id: string
  name: string
  type: 'local' | 'remote'
  command?: string
  argsJson?: string
  cwd?: string
  envJson?: string
  autoStart?: boolean
  url?: string
  authType?: string
  authRef?: string
  createdAt: string
  updatedAt: string
  status: 'stopped' | 'connecting' | 'connected' | 'error'
}

export interface McpTool {
  name: string
  description?: string
  inputSchema?: Record<string, unknown>
}

export interface BridgeRequest {
  id: string
  method: string
  params?: Record<string, unknown>
}

export interface BridgeResponse {
  id: string
  result?: unknown
  error?: { message: string }
}
