import { useState, useRef, useEffect } from 'react'
import { useParams } from 'react-router-dom'
import { Button, Select } from '@fluentui/react-components'
import { SendRegular, StopRegular } from '@fluentui/react-icons'
import ReactMarkdown from 'react-markdown'
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter'
import { vscDarkPlus } from 'react-syntax-highlighter/dist/esm/styles/prism'
import { useStore } from '../store'
import StreamingText from '../components/StreamingText'

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
  const [selectedModel, setSelectedModel] = useState('gpt-5.2')
  const [reasoningEffort, setReasoningEffort] = useState('medium')
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
          <h2>Welcome to Chloye</h2>
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
