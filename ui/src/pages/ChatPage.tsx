import { useState, useRef, useEffect, useMemo } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import {
  Button,
  Menu,
  MenuTrigger,
  MenuList,
  MenuItem,
  MenuPopover,
  MenuDivider,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogActions,
  DialogContent,
  Input,
} from '@fluentui/react-components'
import {
  SendRegular,
  StopRegular,
  ChevronDownRegular,
  ChevronRightRegular,
  StarRegular,
  StarFilled,
  EditRegular,
  DeleteRegular,
  FolderAddRegular
} from '@fluentui/react-icons'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter'
import { vscDarkPlus } from 'react-syntax-highlighter/dist/esm/styles/prism'
import { useStore } from '../store'
import StreamingText from '../components/StreamingText'
import type { ToolCallDetail, TokenUsage } from '../types'

export default function ChatPage() {
  const { conversationId } = useParams()
  const navigate = useNavigate()
  const [input, setInput] = useState('')
  const [selectedModel, setSelectedModel] = useState('openai/gpt-5-mini')
  const messagesEndRef = useRef<HTMLDivElement>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const messages = useStore((state) => state.messages)
  const conversations = useStore((state) => state.conversations)
  const isStreaming = useStore((state) => state.isStreaming)
  const streamingMessageId = useStore((state) => state.streamingMessageId)
  const currentConversationId = useStore((state) => state.currentConversationId)
  const selectConversation = useStore((state) => state.selectConversation)
  const sendMessage = useStore((state) => state.sendMessage)
  const stopStream = useStore((state) => state.stopStream)
  const hasApiKey = useStore((state) => state.hasApiKey)
  const checkApiKey = useStore((state) => state.checkApiKey)
  const availableModels = useStore((state) => state.availableModels)
  const loadModels = useStore((state) => state.loadModels)
  const togglePinConversation = useStore((state) => state.togglePinConversation)
  const renameConversation = useStore((state) => state.renameConversation)
  const deleteConversation = useStore((state) => state.deleteConversation)

  const currentConversation = conversations.find(c => c.id === currentConversationId)

  // Group models by provider for the dropdown
  const modelsByProvider = useMemo(() => {
    const grouped: Record<string, typeof availableModels> = {}
    for (const model of availableModels) {
      if (!grouped[model.provider]) {
        grouped[model.provider] = []
      }
      grouped[model.provider].push(model)
    }
    return grouped
  }, [availableModels])

  useEffect(() => {
    checkApiKey()
    loadModels()
  }, [checkApiKey, loadModels])

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
    await sendMessage(content, selectedModel)
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

  const getModelDisplayName = (modelId: string) => {
    const model = availableModels.find(m => m.id === modelId)
    return model?.name || modelId.split('/').pop() || modelId
  }

  const [isRenameDialogOpen, setIsRenameDialogOpen] = useState(false)
  const [renameInput, setRenameInput] = useState('')
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false)

  const handlePin = async () => {
    if (currentConversation) {
      await togglePinConversation(currentConversation.id, !currentConversation.isPinned)
    }
  }

  const handleRenameClick = () => {
    if (currentConversation) {
      setRenameInput(currentConversation.title)
      setIsRenameDialogOpen(true)
    }
  }

  const onRenameSubmit = async () => {
    if (currentConversation && renameInput.trim()) {
      await renameConversation(currentConversation.id, renameInput.trim())
      setIsRenameDialogOpen(false)
    }
  }

  const handleDeleteClick = () => {
    if (currentConversation) {
      setIsDeleteDialogOpen(true)
    }
  }

  const onDeleteConfirm = async () => {
    if (currentConversation) {
      await deleteConversation(currentConversation.id)
      setIsDeleteDialogOpen(false)
      navigate('/')
    }
  }

  // Reuseable components
  const modelSelector = (
    <Menu positioning="below-end">
      <MenuTrigger disableButtonEnhancement>
        <Button
          appearance="subtle"
          className="model-selector-btn"
          icon={<ChevronDownRegular />}
          iconPosition="after"
          size="small"
        >
          {getModelDisplayName(selectedModel)}
        </Button>
      </MenuTrigger>
      <MenuPopover>
        <MenuList style={{ maxHeight: '300px', overflowY: 'auto' }}>
          {Object.entries(modelsByProvider).map(([provider, models]) => (
            <div key={provider}>
              <div style={{ padding: '8px 12px', fontSize: '12px', color: '#888', fontWeight: 600 }}>
                {provider.toUpperCase()}
              </div>
              {models.map((model) => (
                <MenuItem
                  key={model.id}
                  onClick={() => setSelectedModel(model.id)}
                >
                  {model.name}
                </MenuItem>
              ))}
              <MenuDivider />
            </div>
          ))}
        </MenuList>
      </MenuPopover>
    </Menu>
  )

  const composerBox = (
    <div className="composer-box">
      <textarea
        ref={textareaRef}
        className="composer-textarea"
        value={input}
        onChange={(e) => {
          setInput(e.target.value)
          adjustTextareaHeight()
        }}
        onKeyDown={handleKeyDown}
        placeholder="How can I help you today?"
        rows={1}
        disabled={isStreaming}
      />
      <div className="composer-footer">
        <div className="composer-actions-left">
          {/* Add +, Code, etc buttons here later if needed */}
        </div>
        <div className="composer-controls-right">
          {modelSelector}
          {isStreaming ? (
            <Button
              icon={<StopRegular />}
              appearance="secondary"
              className="send-button"
              onClick={stopStream}
              shape="circular"
            />
          ) : (
            <Button
              icon={<SendRegular />}
              appearance={input.trim() ? "primary" : "subtle"}
              className="send-button"
              onClick={handleSend}
              disabled={!input.trim() || !hasApiKey}
              shape="circular"
            />
          )}
        </div>
      </div>
    </div>
  )

  // -- VIEWS --

  // 1. Empty State (Centered)
  if (messages.length === 0) {
    return (
      <div className="chat-container centered-view">
        <div className="centered-content">
          <h2 className="greeting-title">Joyee is thinking</h2>
          <div className="composer-wrapper centered">
            {composerBox}
          </div>
          {!hasApiKey && (
            <p className="api-key-warning">
              ⚠️ Please set your OpenRouter API key in Settings to start chatting.
            </p>
          )}
        </div>
      </div>
    )
  }

  // 2. Active Chat State
  return (
    <div className="chat-container">
      {currentConversation && (
        <div className="chat-header">
          <div className="chat-title-group">
            <h2 className="chat-title">{currentConversation.title}</h2>
            <Menu>
              <MenuTrigger disableButtonEnhancement>
                <Button appearance="subtle" icon={<ChevronDownRegular />} size="small" />
              </MenuTrigger>
              <MenuPopover className="chat-menu-popover">
                <MenuList>
                  <MenuItem
                    icon={currentConversation.isPinned ? <StarFilled /> : <StarRegular />}
                    onClick={handlePin}
                  >
                    {currentConversation.isPinned ? "Unstar" : "Star"}
                  </MenuItem>
                  <MenuItem icon={<EditRegular />} onClick={handleRenameClick}>
                    Rename
                  </MenuItem>
                  <MenuItem icon={<FolderAddRegular />} disabled>
                    Add to project
                  </MenuItem>
                  <MenuDivider />
                  <MenuItem
                    icon={<DeleteRegular />}
                    className="delete-menu-item"
                    onClick={handleDeleteClick}
                  >
                    Delete
                  </MenuItem>
                </MenuList>
              </MenuPopover>
            </Menu>
          </div>
        </div>
      )}
      <div className="messages-area">
        {messages.map((message) => (
          <div key={message.id} className={`message ${message.role}`}>
            <div className="message-role">
              {message.role === 'user' ? 'You' : 'Assistant'}
              {message.model && (
                <span className="message-model-tag">
                  ({getModelDisplayName(message.model)})
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

      <div className="composer-wrapper bottom">
        {composerBox}
      </div>

      <Dialog open={isRenameDialogOpen} onOpenChange={(_, { open }) => setIsRenameDialogOpen(open)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Rename Conversation</DialogTitle>
            <DialogContent>
              <Input
                value={renameInput}
                onChange={(_e, data) => setRenameInput(data.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    onRenameSubmit()
                  }
                }}
                style={{ width: '100%' }}
                autoFocus
                placeholder="Conversation title"
              />
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setIsRenameDialogOpen(false)}>Cancel</Button>
              <Button appearance="primary" onClick={onRenameSubmit}>Save</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      <Dialog open={isDeleteDialogOpen} onOpenChange={(_, { open }) => setIsDeleteDialogOpen(open)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Delete Conversation</DialogTitle>
            <DialogContent>
              Are you sure you want to delete this conversation? This action cannot be undone.
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setIsDeleteDialogOpen(false)}>Cancel</Button>
              <Button appearance="primary" onClick={onDeleteConfirm}>Delete</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div >
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
  // Calculate output tokens excluding reasoning
  const outputMinusReasoning = usage.outputTokens - usage.reasoningTokens

  // Format cost - show up to 6 decimal places for small amounts
  const formatCost = (cost: number) => {
    if (cost === 0) return '$0.00'
    if (cost < 0.01) return `$${cost.toFixed(6)}`
    if (cost < 0.1) return `$${cost.toFixed(4)}`
    return `$${cost.toFixed(2)}`
  }

  return (
    <div className="token-usage">
      <span className="token-usage-item">
        <span className="token-usage-label">📥 Input:</span> {usage.inputTokens.toLocaleString()}
      </span>
      {usage.reasoningTokens > 0 && (
        <span className="token-usage-item">
          <span className="token-usage-label">🧠 Reasoning:</span> {usage.reasoningTokens.toLocaleString()}
        </span>
      )}
      <span className="token-usage-item">
        <span className="token-usage-label">📤 Output:</span> {outputMinusReasoning.toLocaleString()}
      </span>
      <span className="token-usage-item token-usage-total">
        <span className="token-usage-label">📊 Total:</span> {usage.totalTokens.toLocaleString()}
      </span>
      {usage.cost > 0 && (
        <span className="token-usage-item token-usage-cost">
          <span className="token-usage-label">💰 Cost:</span> {formatCost(usage.cost)}
        </span>
      )}
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
