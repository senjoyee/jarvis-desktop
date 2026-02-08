import { useEffect, useRef, useState, useCallback } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter'
import { vscDarkPlus } from 'react-syntax-highlighter/dist/esm/styles/prism'

interface StreamingTextProps {
  messageId: string
  initialContent: string
}

// Content buffers and React update callbacks
const contentBuffers = new Map<string, string>()
const updateCallbacks = new Map<string, () => void>()
const throttleTimers = new Map<string, number>()

const THROTTLE_MS = 100 // Render markdown every 100ms

export function appendToStreamingText(messageId: string, delta: string) {
  // Accumulate content
  const current = contentBuffers.get(messageId) || ''
  contentBuffers.set(messageId, current + delta)

  // Throttled React update for markdown rendering
  if (!throttleTimers.has(messageId)) {
    const timer = window.setTimeout(() => {
      throttleTimers.delete(messageId)
      const callback = updateCallbacks.get(messageId)
      if (callback) callback()
    }, THROTTLE_MS)
    throttleTimers.set(messageId, timer)
  }
}

export function getStreamingContent(messageId: string): string {
  return contentBuffers.get(messageId) || ''
}

export function clearStreamingRefs() {
  contentBuffers.clear()
  updateCallbacks.clear()
  throttleTimers.forEach(timer => clearTimeout(timer))
  throttleTimers.clear()
}

export default function StreamingText({ messageId, initialContent }: StreamingTextProps) {
  const [, forceUpdate] = useState(0)
  const mounted = useRef(false)

  const triggerUpdate = useCallback(() => {
    forceUpdate(n => n + 1)
  }, [])

  useEffect(() => {
    // Initialize buffer with initial content on first mount
    if (!mounted.current) {
      contentBuffers.set(messageId, initialContent)
      mounted.current = true
    }

    // Register callback for throttled updates
    updateCallbacks.set(messageId, triggerUpdate)

    return () => {
      updateCallbacks.delete(messageId)
      const timer = throttleTimers.get(messageId)
      if (timer) {
        clearTimeout(timer)
        throttleTimers.delete(messageId)
      }
    }
  }, [messageId, initialContent, triggerUpdate])

  const content = contentBuffers.get(messageId) || initialContent || '...'

  return (
    <ReactMarkdown
      remarkPlugins={[remarkGfm]}
      components={{
        code({ className, children, ...props }) {
          const match = /language-(\w+)/.exec(className || '')
          const isInline = !match
          return isInline ? (
            <code className={className} {...props}>
              {children}
            </code>
          ) : (
            <SyntaxHighlighter
              style={vscDarkPlus}
              language={match[1]}
              PreTag="div"
            >
              {String(children).replace(/\n$/, '')}
            </SyntaxHighlighter>
          )
        }
      }}
    >
      {content}
    </ReactMarkdown>
  )
}
