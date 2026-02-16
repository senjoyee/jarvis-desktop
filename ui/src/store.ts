import { create } from 'zustand'
import { invoke, setStreamHandlers } from './services/bridge'
import type { Conversation, Message, McpServer, McpTool, ToolCallDetail, TokenUsage, ModelDefinition } from './types'

interface AppState {
  // Conversations
  conversations: Conversation[]
  currentConversationId: string | null
  messages: Message[]
  isLoading: boolean
  isStreaming: boolean
  streamingMessageId: string | null

  // Internal streaming batching state
  _pendingDelta: string
  _rafId: number | null
  _streamingMsgId: string | null

  // MCP
  mcpServers: McpServer[]

  // Settings
  hasApiKey: boolean
  availableModels: ModelDefinition[]
  codeMode: boolean

  // Actions
  loadConversations: () => Promise<void>
  createConversation: (title?: string) => Promise<Conversation>
  deleteConversation: (id: string) => Promise<void>
  renameConversation: (id: string, title: string) => Promise<void>
  togglePinConversation: (id: string, isPinned: boolean) => Promise<void>
  selectConversation: (id: string | null) => Promise<void>

  loadMessages: (conversationId: string) => Promise<void>
  sendMessage: (content: string, model: string) => Promise<void>
  stopStream: () => Promise<void>

  // Streaming handlers
  handleStreamStart: (messageId: string) => void
  handleStreamDelta: (messageId: string, delta: string) => void
  handleStreamDone: (messageId: string, usage?: TokenUsage) => void
  handleStreamReasoning: (messageId: string, delta: string) => void
  handleStreamToolCallStart: (messageId: string, toolName: string, args: string) => void
  handleStreamToolCallResult: (messageId: string, toolName: string, result: string, success: boolean) => void

  loadMcpServers: () => Promise<void>
  addMcpServer: (config: Partial<McpServer>) => Promise<void>
  removeMcpServer: (id: string) => Promise<void>
  startMcpServer: (id: string) => Promise<void>
  stopMcpServer: (id: string) => Promise<void>
  getMcpLogs: (id: string) => Promise<string[]>
  getMcpTools: (id: string) => Promise<McpTool[]>
  callMcpTool: (serverId: string, toolName: string, args: Record<string, unknown>) => Promise<unknown>

  checkApiKey: () => Promise<void>
  setApiKey: (key: string) => Promise<void>
  clearApiKey: () => Promise<void>
  testOpenRouter: () => Promise<boolean>
  loadModels: () => Promise<void>
  toggleCodeMode: () => void
}

export const useStore = create<AppState>((set, get) => ({
  conversations: [],
  currentConversationId: null,
  messages: [],
  isLoading: false,
  isStreaming: false,
  streamingMessageId: null,
  _pendingDelta: '',
  _rafId: null,
  _streamingMsgId: null,
  mcpServers: [],
  hasApiKey: false,
  availableModels: [],
  codeMode: false,

  loadConversations: async () => {
    try {
      const conversations = await invoke<Conversation[]>('conversations.list')
      set({ conversations })
    } catch (err) {
      console.error('Failed to load conversations:', err)
    }
  },

  createConversation: async (title = 'New Conversation') => {
    const conversation = await invoke<Conversation>('conversations.create', { title })
    set((state) => ({
      conversations: [conversation, ...state.conversations],
      currentConversationId: conversation.id,
      messages: []
    }))
    return conversation
  },

  deleteConversation: async (id) => {
    await invoke('conversations.delete', { id })
    set((state) => {
      const conversations = state.conversations.filter((c) => c.id !== id)
      const currentConversationId = state.currentConversationId === id
        ? (conversations[0]?.id ?? null)
        : state.currentConversationId
      return { conversations, currentConversationId }
    })

    const { currentConversationId } = get()
    if (currentConversationId) {
      await get().loadMessages(currentConversationId)
    } else {
      set({ messages: [] })
    }
  },

  renameConversation: async (id, title) => {
    await invoke('conversations.rename', { id, title })
    set((state) => ({
      conversations: state.conversations.map((c) =>
        c.id === id ? { ...c, title } : c
      )
    }))
  },

  togglePinConversation: async (id, isPinned) => {
    await invoke('conversations.pin', { id, isPinned })
    set((state) => ({
      conversations: state.conversations.map((c) =>
        c.id === id ? { ...c, isPinned } : c
      )
    }))
  },


  selectConversation: async (id) => {
    set({ currentConversationId: id, messages: [] })
    if (id) {
      await get().loadMessages(id)
    }
  },

  loadMessages: async (conversationId) => {
    set({ isLoading: true })
    try {
      const messages = await invoke<Message[]>('messages.list', { conversationId })
      set({ messages, isLoading: false })
    } catch (err) {
      console.error('Failed to load messages:', err)
      set({ isLoading: false })
    }
  },

  sendMessage: async (content, model) => {
    let { currentConversationId } = get()

    if (!currentConversationId) {
      const conv = await get().createConversation()
      currentConversationId = conv.id
    }

    // Add user message immediately for instant feedback
    const tempUserMsg: Message = {
      id: `temp-user-${Date.now()}`,
      conversationId: currentConversationId,
      role: 'user',
      content,
      model,
      createdAt: new Date().toISOString()
    }

    // Add placeholder assistant message that streaming will fill
    const tempAssistantMsg: Message = {
      id: `temp-assistant-${Date.now()}`,
      conversationId: currentConversationId,
      role: 'assistant',
      content: '',
      model,
      createdAt: new Date().toISOString()
    }

    set((state) => ({
      messages: [...state.messages, tempUserMsg, tempAssistantMsg],
      isStreaming: true
    }))

    try {
      const result = await invoke<{ userMessage: Message; assistantMessage: Message }>(
        'messages.send',
        { conversationId: currentConversationId, content, model, codeMode: get().codeMode }
      )

      const realAssistantId = result.assistantMessage.id?.toString()

      // Replace temp messages with real ones (with correct IDs from backend)
      // Preserve reasoning and toolCalls accumulated during streaming
      set((state) => ({
        messages: state.messages.map((msg) => {
          if (msg.id === tempUserMsg.id) return result.userMessage
          if (msg.id === tempAssistantMsg.id || msg.id === realAssistantId) {
            return {
              ...result.assistantMessage,
              reasoning: msg.reasoning,
              toolCalls: msg.toolCalls,
              tokenUsage: msg.tokenUsage
            }
          }
          return msg
        }),
        isStreaming: false
      }))

      // Update conversation title if it's the first message
      const { messages } = get()
      if (messages.length <= 2) {
        const newTitle = content.slice(0, 50) + (content.length > 50 ? '...' : '')
        await get().renameConversation(currentConversationId!, newTitle)
      }
    } catch (err) {
      console.error('Failed to send message:', err)
      // Keep user message but replace assistant placeholder with error message
      // The temp ID may have been replaced by handleStreamStart, so check both
      const errorMessage = err instanceof Error ? err.message : 'An error occurred'
      const streamingId = get()._streamingMsgId
      set((state) => ({
        messages: state.messages.map((msg) => {
          if (msg.id === tempAssistantMsg.id || msg.id === streamingId) {
            return { ...msg, content: (msg.content || '') + `\n\n⚠️ Error: ${errorMessage}` }
          }
          return msg
        }),
        isStreaming: false
      }))
    }
  },

  stopStream: async () => {
    await invoke('messages.stopStream')
    set({ isStreaming: false })
  },

  loadMcpServers: async () => {
    try {
      const mcpServers = await invoke<McpServer[]>('mcp.list')
      set({ mcpServers })
    } catch (err) {
      console.error('Failed to load MCP servers:', err)
    }
  },

  addMcpServer: async (config) => {
    await invoke('mcp.add', config)
    await get().loadMcpServers()
  },

  removeMcpServer: async (id) => {
    await invoke('mcp.remove', { id })
    await get().loadMcpServers()
  },

  startMcpServer: async (id) => {
    await invoke('mcp.start', { id })
    await get().loadMcpServers()
  },

  stopMcpServer: async (id) => {
    await invoke('mcp.stop', { id })
    await get().loadMcpServers()
  },

  getMcpLogs: async (id) => {
    return await invoke<string[]>('mcp.logs', { id })
  },

  getMcpTools: async (id) => {
    return await invoke<McpTool[]>('mcp.tools.list', { serverId: id })
  },

  callMcpTool: async (serverId, toolName, args) => {
    return await invoke('mcp.tools.call', { serverId, toolName, args })
  },

  checkApiKey: async () => {
    const hasApiKey = await invoke<boolean>('settings.hasApiKey')
    set({ hasApiKey })
  },

  setApiKey: async (key) => {
    await invoke('settings.setApiKey', { key })
    set({ hasApiKey: true })
  },

  clearApiKey: async () => {
    await invoke('settings.clearApiKey')
    set({ hasApiKey: false })
  },

  testOpenRouter: async () => {
    return await invoke<boolean>('settings.testOpenRouter')
  },

  loadModels: async () => {
    try {
      const models = await invoke<ModelDefinition[]>('models.list')
      set({ availableModels: models })
    } catch (err) {
      console.error('Failed to load models:', err)
    }
  },

  // Streaming handlers
  handleStreamStart: (messageId) => {
    // Update the temp assistant message ID to the real one from backend
    set((state) => {
      const tempAssistant = state.messages.find(
        (m) => m.role === 'assistant' && m.id.startsWith('temp-assistant-')
      )
      if (tempAssistant) {
        return {
          streamingMessageId: messageId,
          isStreaming: true,
          _streamingMsgId: messageId,
          messages: state.messages.map((msg) =>
            msg.id === tempAssistant.id ? { ...msg, id: messageId } : msg
          )
        }
      }
      return { streamingMessageId: messageId, isStreaming: true, _streamingMsgId: messageId }
    })
  },

  handleStreamDelta: (messageId, delta) => {
    // Direct update - no batching for now
    set((state) => {
      const msgIndex = state.messages.findIndex((m) => m.id === messageId)
      if (msgIndex === -1) return state
      const newMessages = [...state.messages]
      newMessages[msgIndex] = {
        ...newMessages[msgIndex],
        content: (newMessages[msgIndex].content || '') + delta
      }
      return { messages: newMessages }
    })
  },

  handleStreamDone: (_messageId, usage) => {
    console.log('[Store] handleStreamDone called, messageId:', _messageId, 'usage:', usage)
    if (usage) {
      set((state) => {
        const msgIndex = state.messages.findIndex((m) => m.id === _messageId)
        console.log('[Store] Updating message at index:', msgIndex, 'with tokenUsage:', usage)
        if (msgIndex === -1) return state
        const newMessages = [...state.messages]
        newMessages[msgIndex] = { ...newMessages[msgIndex], tokenUsage: usage }
        return { messages: newMessages }
      })
    }
    set({ streamingMessageId: null, isStreaming: false, _pendingDelta: '', _rafId: null, _streamingMsgId: null })
  },

  handleStreamReasoning: (messageId, delta) => {
    set((state) => {
      const msgIndex = state.messages.findIndex((m) => m.id === messageId)
      if (msgIndex === -1) return state
      const newMessages = [...state.messages]
      newMessages[msgIndex] = {
        ...newMessages[msgIndex],
        reasoning: (newMessages[msgIndex].reasoning || '') + delta
      }
      return { messages: newMessages }
    })
  },

  handleStreamToolCallStart: (messageId, toolName, args) => {
    set((state) => {
      const msgIndex = state.messages.findIndex((m) => m.id === messageId)
      if (msgIndex === -1) return state
      const newMessages = [...state.messages]
      const existing = newMessages[msgIndex].toolCalls || []
      const newToolCall: ToolCallDetail = {
        toolName,
        arguments: args,
        status: 'calling'
      }
      newMessages[msgIndex] = {
        ...newMessages[msgIndex],
        toolCalls: [...existing, newToolCall]
      }
      return { messages: newMessages }
    })
  },

  handleStreamToolCallResult: (messageId, toolName, result, success) => {
    set((state) => {
      const msgIndex = state.messages.findIndex((m) => m.id === messageId)
      if (msgIndex === -1) return state
      const newMessages = [...state.messages]
      const toolCalls = [...(newMessages[msgIndex].toolCalls || [])]
      // Find the last tool call with this name that is still 'calling'
      let tcIndex = -1
      for (let i = toolCalls.length - 1; i >= 0; i--) {
        if (toolCalls[i].toolName === toolName && toolCalls[i].status === 'calling') {
          tcIndex = i
          break
        }
      }
      if (tcIndex !== -1) {
        toolCalls[tcIndex] = { ...toolCalls[tcIndex], result, success, status: 'done' }
      }
      newMessages[msgIndex] = { ...newMessages[msgIndex], toolCalls }
      return { messages: newMessages }
    })
  },

  toggleCodeMode: () => {
    set((state) => ({ codeMode: !state.codeMode }))
  }
}))

// Initialize stream handlers when store is created
setStreamHandlers({
  onStart: (data) => useStore.getState().handleStreamStart(data.messageId),
  onDelta: (data) => useStore.getState().handleStreamDelta(data.messageId, data.delta || ''),
  onDone: (data) => useStore.getState().handleStreamDone(data.messageId, data.usage),
  onReasoning: (data) => useStore.getState().handleStreamReasoning(data.messageId, data.delta || ''),
  onToolCallStart: (data) => useStore.getState().handleStreamToolCallStart(data.messageId, data.toolName, data.arguments),
  onToolCallResult: (data) => useStore.getState().handleStreamToolCallResult(data.messageId, data.toolName, data.result, data.success)
})
