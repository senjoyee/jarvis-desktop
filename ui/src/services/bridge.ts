import { v4 as uuidv4 } from 'uuid'
import type { BridgeRequest, BridgeResponse, TokenUsage } from '../types'
import { appendToStreamingText } from '../components/StreamingText'

const pendingRequests = new Map<string, { resolve: (value: unknown) => void; reject: (error: Error) => void }>()

// Stream event handlers
type StreamEventHandler = (data: { messageId: string; delta?: string }) => void
type StreamDoneHandler = (data: { messageId: string; usage?: TokenUsage }) => void
type ToolCallStartHandler = (data: { messageId: string; toolName: string; arguments: string }) => void
type ToolCallResultHandler = (data: { messageId: string; toolName: string; result: string; success: boolean }) => void

let onStreamStart: StreamEventHandler | null = null
let onStreamDelta: StreamEventHandler | null = null
let onStreamDone: StreamDoneHandler | null = null
let onStreamReasoning: StreamEventHandler | null = null
let onStreamToolCallStart: ToolCallStartHandler | null = null
let onStreamToolCallResult: ToolCallResultHandler | null = null

export function setStreamHandlers(handlers: {
  onStart?: StreamEventHandler
  onDelta?: StreamEventHandler
  onDone?: StreamDoneHandler
  onReasoning?: StreamEventHandler
  onToolCallStart?: ToolCallStartHandler
  onToolCallResult?: ToolCallResultHandler
}) {
  onStreamStart = handlers.onStart || null
  onStreamDelta = handlers.onDelta || null
  onStreamDone = handlers.onDone || null
  onStreamReasoning = handlers.onReasoning || null
  onStreamToolCallStart = handlers.onToolCallStart || null
  onStreamToolCallResult = handlers.onToolCallResult || null
}

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (message: string) => void
        addEventListener: (event: string, handler: (e: { data: string }) => void) => void
      }
    }
  }
}

function initBridge() {
  console.log('[Bridge] Initializing bridge, webview available:', !!window.chrome?.webview)
  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener('message', (e) => {
      console.log('[Bridge] Raw message received:', e.data?.substring(0, 200))
      try {
        const message = JSON.parse(e.data)
        
        // Check if it's a stream event
        if (message.type?.startsWith('stream.')) {
          console.log('[Bridge] Stream event detected:', message.type)
          handleStreamEvent(message)
          return
        }
        
        // Otherwise it's a response to a request
        const response: BridgeResponse = message
        if (response.id && pendingRequests.has(response.id)) {
          const { resolve, reject } = pendingRequests.get(response.id)!
          pendingRequests.delete(response.id)
          
          if (response.error) {
            reject(new Error(response.error.message))
          } else {
            resolve(response.result)
          }
        }
      } catch (err) {
        console.error('Failed to parse bridge response:', err)
      }
    })
  }
}

function handleStreamEvent(event: { type: string; data: Record<string, unknown> }) {
  switch (event.type) {
    case 'stream.start':
      onStreamStart?.(event.data as { messageId: string; delta?: string })
      break
    case 'stream.delta':
      // Accumulate content for throttled markdown rendering
      appendToStreamingText((event.data as { messageId: string }).messageId, (event.data as { delta?: string }).delta || '')
      // Also update store for final state
      onStreamDelta?.(event.data as { messageId: string; delta?: string })
      break
    case 'stream.done':
      onStreamDone?.(event.data as { messageId: string; usage?: TokenUsage })
      break
    case 'stream.reasoning':
      onStreamReasoning?.(event.data as { messageId: string; delta?: string })
      break
    case 'stream.toolCallStart':
      onStreamToolCallStart?.(event.data as { messageId: string; toolName: string; arguments: string })
      break
    case 'stream.toolCallResult':
      onStreamToolCallResult?.(event.data as { messageId: string; toolName: string; result: string; success: boolean })
      break
  }
}

initBridge()

export async function invoke<T = unknown>(method: string, params?: Record<string, unknown>): Promise<T> {
  const id = uuidv4()
  const request: BridgeRequest = { id, method, params }

  return new Promise((resolve, reject) => {
    pendingRequests.set(id, { resolve: resolve as (value: unknown) => void, reject })

    if (window.chrome?.webview) {
      window.chrome.webview.postMessage(JSON.stringify(request))
    } else {
      // Mock for browser development
      console.log('[Bridge Mock]', method, params)
      setTimeout(() => {
        pendingRequests.delete(id)
        resolve(getMockResponse(method, params) as T)
      }, 100)
    }

    // Timeout - longer for message sends (reasoning can take minutes)
    const timeoutMs = method === 'messages.send' ? 300000 : 30000
    setTimeout(() => {
      if (pendingRequests.has(id)) {
        pendingRequests.delete(id)
        reject(new Error('Request timeout'))
      }
    }, timeoutMs)
  })
}

function getMockResponse(method: string, params?: Record<string, unknown>): unknown {
  switch (method) {
    case 'conversations.list':
      return []
    case 'conversations.create':
      return {
        id: uuidv4(),
        title: params?.title || 'New Conversation',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      }
    case 'messages.list':
      return []
    case 'messages.send':
      return {
        userMessage: {
          id: uuidv4(),
          conversationId: params?.conversationId,
          role: 'user',
          content: params?.content,
          model: params?.model,
          createdAt: new Date().toISOString()
        },
        assistantMessage: {
          id: uuidv4(),
          conversationId: params?.conversationId,
          role: 'assistant',
          content: 'This is a mock response. Connect to the WPF host for real functionality.',
          model: params?.model,
          createdAt: new Date().toISOString()
        }
      }
    case 'settings.hasApiKey':
      return false
    case 'settings.testOpenAI':
      return false
    case 'mcp.list':
      return []
    case 'mcp.tools.list':
      return []
    default:
      return null
  }
}
