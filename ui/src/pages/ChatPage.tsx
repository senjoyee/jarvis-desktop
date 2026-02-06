import { useState, useRef, useEffect } from 'react'
import { useParams } from 'react-router-dom'
import { Button, Select } from '@fluentui/react-components'
import { SendRegular, StopRegular, ChevronDownRegular, ChevronRightRegular } from '@fluentui/react-icons'
import ReactMarkdown from 'react-markdown'
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter'
import { vscDarkPlus } from 'react-syntax-highlighter/dist/esm/styles/prism'
import { useStore } from '../store'
import StreamingText from '../components/StreamingText'
import type { ToolCallDetail, TokenUsage } from '../types'

const AVAILABLE_MODELS = [
  { value: 'gpt-5.2', label: 'GPT-5.2' },
  { value: 'gpt-5-mini', label: 'GPT-5 Mini' },
]

const REASONING_EFFORT_LEVELS = [
  { value: 'none', label: 'None (Fastest)' },
  { value: 'low', label: 'Low' },
  { value: 'medium', label: 'Medium' },
  { value: 'high', label: 'High (Most Thorough)' },
]

export default function ChatPage() {
  const { conversationId } = useParams()
  const [input, setInput] = useState('')
  const [selectedModel, setSelectedModel] = useState('gpt-5-mini')
  const [reasoningEffort, setReasoningEffort] = useState('none')
  const messagesEndRef = useRef<HTMLDivElement>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const messages = useStore((state) => state.messages)
  const isStreaming = useStore((state) => state.isStreaming)
  const streamingMessageId = useStore((state) => state.streamingMessageId)
  const currentConversationId = useStore((state) => state.currentConversationId)
  const selectConversation = useStore((state) => state.selectConversation)
  const sendMessage = useStore((state) => state.sendMessage)
  const stopStream = useStore((state) => state.stopStream)
  const hasApiKey = useStore((state) => state.hasApiKey)
  const checkApiKey = useStore((state) => state.checkApiKey)

  useEffect(() => {
    checkApiKey()
  }, [checkApiKey])

  useEffect(() => {
    if (conversationId && conversationId !== currentConversationId) {
      selectConversation(conversationId)
    }
  }, [conversationId, currentConversationId, selectConversation])

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  const handleSend = async () => {
    if (!input.trim() || isStreaming) return

    const content = input.trim()
    setInput('')
    await sendMessage(content, selectedModel, reasoningEffort)
  }

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  const adjustTextareaHeight = () => {
    if (textareaRef.current) {
      textareaRef.current.style.height = 'auto'
      textareaRef.current.style.height = Math.min(textareaRef.current.scrollHeight, 200) + 'px'
    }
  }

  if (!currentConversationId && messages.length === 0) {
    return (
      <div className="chat-container">
        <div className="empty-state">
          <h2>Welcome to Jarvis</h2>
          <p>Start a new conversation or select one from the sidebar.</p>
          {!hasApiKey && (
            <p style={{ marginTop: 16, color: '#ffc107' }}>
              ⚠️ Please set your OpenAI API key in Settings to start chatting.
            </p>
          )}
        </div>

        <div className="composer">
          <div className="composer-inner">
            <div className="model-selector">
              <Select
                value={selectedModel}
                onChange={(_, data) => setSelectedModel(data.value)}
                size="small"
              >
                {AVAILABLE_MODELS.map((model) => (
                  <option key={model.value} value={model.value}>
                    {model.label}
                  </option>
                ))}
              </Select>
              <Select
                value={reasoningEffort}
                onChange={(_, data) => setReasoningEffort(data.value)}
                size="small"
              >
                {REASONING_EFFORT_LEVELS.map((level) => (
                  <option key={level.value} value={level.value}>
                    Reasoning: {level.label}
                  </option>
                ))}
              </Select>
            </div>
            <div className="composer-row">
              <div className="composer-input">
                <textarea
                  ref={textareaRef}
                  value={input}
                  onChange={(e) => {
                    setInput(e.target.value)
                    adjustTextareaHeight()
                  }}
                  onKeyDown={handleKeyDown}
                  placeholder="Type a message..."
                  rows={1}
                />
              </div>
              <Button
                icon={<SendRegular />}
                appearance="primary"
                onClick={handleSend}
                disabled={!input.trim() || !hasApiKey}
              />
            </div>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="chat-container">
      <div className="messages-area">
        {messages.map((message) => (
          <div key={message.id} className={`message ${message.role}`}>
            <div className="message-role">
              {message.role === 'user' ? 'You' : 'Assistant'}
              {message.model && (
                <span className="message-model-tag">
                  ({message.model})
                </span>
              )}
            </div>

            {message.role === 'assistant' && message.reasoning && (
              <ReasoningBlock reasoning={message.reasoning} isStreaming={isStreaming && message.id === streamingMessageId} />
            )}

            {message.role === 'assistant' && message.toolCalls && message.toolCalls.length > 0 && (
              <div className="tool-calls-section">
                {message.toolCalls.map((tc, idx) => (
                  <ToolCallBlock key={`${message.id}-tc-${idx}`} toolCall={tc} />
                ))}
              </div>
            )}

            <div className="message-content">
              {isStreaming && message.id === streamingMessageId ? (
                <StreamingText messageId={message.id} initialContent={message.content || ''} />
              ) : (
                <ReactMarkdown
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
                  {message.content || '...'}
                </ReactMarkdown>
              )}
            </div>

            {message.role === 'assistant' && message.tokenUsage && (
              <TokenUsageDisplay usage={message.tokenUsage} />
            )}
          </div>
        ))}
        <div ref={messagesEndRef} />
      </div>

      <div className="composer">
        <div className="composer-inner">
          <div className="model-selector">
            <Select
              value={selectedModel}
              onChange={(_, data) => setSelectedModel(data.value)}
              size="small"
            >
              {AVAILABLE_MODELS.map((model) => (
                <option key={model.value} value={model.value}>
                  {model.label}
                </option>
              ))}
            </Select>
            <Select
              value={reasoningEffort}
              onChange={(_, data) => setReasoningEffort(data.value)}
              size="small"
            >
              {REASONING_EFFORT_LEVELS.map((level) => (
                <option key={level.value} value={level.value}>
                  Reasoning: {level.label}
                </option>
              ))}
            </Select>
          </div>
          <div className="composer-row">
            <div className="composer-input">
              <textarea
                ref={textareaRef}
                value={input}
                onChange={(e) => {
                  setInput(e.target.value)
                  adjustTextareaHeight()
                }}
                onKeyDown={handleKeyDown}
                placeholder="Type a message..."
                rows={1}
                disabled={isStreaming}
              />
            </div>
            {isStreaming ? (
              <Button
                icon={<StopRegular />}
                appearance="secondary"
                onClick={stopStream}
              >
                Stop
              </Button>
            ) : (
              <Button
                icon={<SendRegular />}
                appearance="primary"
                onClick={handleSend}
                disabled={!input.trim() || !hasApiKey}
              />
            )}
          </div>
        </div>
      </div>
    </div>
  )
}

function ReasoningBlock({ reasoning, isStreaming }: { reasoning: string; isStreaming: boolean }) {
  const [expanded, setExpanded] = useState(false)

  // Auto-expand while streaming
  const isOpen = isStreaming || expanded

  return (
    <div className="reasoning-block">
      <button
        className="reasoning-toggle"
        onClick={() => setExpanded(!expanded)}
      >
        {isOpen ? <ChevronDownRegular /> : <ChevronRightRegular />}
        <span className="reasoning-label">
          {isStreaming ? 'Thinking...' : 'Thought process'}
        </span>
        {isStreaming && <span className="reasoning-spinner" />}
      </button>
      {isOpen && (
        <div className="reasoning-content">
          {reasoning}
        </div>
      )}
    </div>
  )
}

function TokenUsageDisplay({ usage }: { usage: TokenUsage }) {
  const outputMinusReasoning = usage.outputTokens - usage.reasoningTokens

  return (
    <div className="token-usage">
      <span className="token-usage-item">
        <span className="token-usage-label">In:</span> {usage.inputTokens.toLocaleString()}
      </span>
      {usage.reasoningTokens > 0 && (
        <span className="token-usage-item">
          <span className="token-usage-label">Reasoning:</span> {usage.reasoningTokens.toLocaleString()}
        </span>
      )}
      <span className="token-usage-item">
        <span className="token-usage-label">Out:</span> {outputMinusReasoning.toLocaleString()}
      </span>
      <span className="token-usage-item token-usage-total">
        <span className="token-usage-label">Total:</span> {usage.totalTokens.toLocaleString()}
      </span>
    </div>
  )
}

function ToolCallBlock({ toolCall }: { toolCall: ToolCallDetail }) {
  const [expanded, setExpanded] = useState(false)

  let parsedArgs = toolCall.arguments
  try {
    parsedArgs = JSON.stringify(JSON.parse(toolCall.arguments), null, 2)
  } catch {
    // keep raw string
  }

  return (
    <div className={`tool-call-block ${toolCall.status}`}>
      <button
        className="tool-call-toggle"
        onClick={() => setExpanded(!expanded)}
      >
        {expanded ? <ChevronDownRegular /> : <ChevronRightRegular />}
        <span className="tool-call-icon">
          {toolCall.status === 'calling' ? '\u2699\uFE0F' : toolCall.success ? '\u2705' : '\u274C'}
        </span>
        <span className="tool-call-name">{toolCall.toolName}</span>
        {toolCall.status === 'calling' && <span className="reasoning-spinner" />}
      </button>
      {expanded && (
        <div className="tool-call-details">
          <div className="tool-call-section">
            <div className="tool-call-section-label">Arguments</div>
            <pre className="tool-call-pre">{parsedArgs}</pre>
          </div>
          {toolCall.result && (
            <div className="tool-call-section">
              <div className="tool-call-section-label">
                Result {toolCall.success === false && <span className="tool-call-error-tag">Error</span>}
              </div>
              <pre className="tool-call-pre">{toolCall.result}</pre>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
