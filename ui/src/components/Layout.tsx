import { Outlet, useNavigate, useLocation } from 'react-router-dom'
import { Button } from '@fluentui/react-components'
import { 
  ChatRegular, 
  SettingsRegular, 
  PlugConnectedRegular,
  AddRegular,
  DeleteRegular
} from '@fluentui/react-icons'
import { useStore } from '../store'
import type { Conversation } from '../types'

export default function Layout() {
  const navigate = useNavigate()
  const location = useLocation()
  
  const conversations = useStore((state) => state.conversations)
  const currentConversationId = useStore((state) => state.currentConversationId)
  const createConversation = useStore((state) => state.createConversation)
  const deleteConversation = useStore((state) => state.deleteConversation)
  const selectConversation = useStore((state) => state.selectConversation)
  const mcpServers = useStore((state) => state.mcpServers)

  const handleNewChat = async () => {
    const conv = await createConversation()
    navigate(`/chat/${conv.id}`)
  }

  const handleSelectConversation = async (conv: Conversation) => {
    await selectConversation(conv.id)
    navigate(`/chat/${conv.id}`)
  }

  const handleDeleteConversation = async (e: React.MouseEvent, id: string) => {
    e.stopPropagation()
    await deleteConversation(id)
  }

  const connectedServers = mcpServers.filter(s => s.status === 'connected').length

  return (
    <div className="app-layout">
      <aside className="sidebar">
        <div className="sidebar-header">
          <span style={{ fontWeight: 600, fontSize: 16 }}>Chloye</span>
          <Button 
            icon={<AddRegular />} 
            appearance="subtle"
            onClick={handleNewChat}
            title="New Chat"
          />
        </div>

        <div className="sidebar-content">
          <div className="sidebar-section">Conversations</div>
          <ul className="conversation-list">
            {conversations.map((conv) => (
              <li 
                key={conv.id}
                className={`conversation-item ${currentConversationId === conv.id ? 'active' : ''}`}
                onClick={() => handleSelectConversation(conv)}
              >
                <ChatRegular />
                <span className="conversation-title">{conv.title}</span>
                <Button
                  icon={<DeleteRegular />}
                  appearance="subtle"
                  size="small"
                  onClick={(e) => handleDeleteConversation(e, conv.id)}
                  style={{ opacity: 0.5 }}
                />
              </li>
            ))}
            {conversations.length === 0 && (
              <li style={{ padding: '10px 16px', color: '#888', fontSize: 13 }}>
                No conversations yet
              </li>
            )}
          </ul>

          <div className="sidebar-section" style={{ marginTop: 24 }}>
            <span>MCP Servers</span>
            {connectedServers > 0 && (
              <span style={{ 
                marginLeft: 8, 
                fontSize: 10, 
                padding: '2px 6px', 
                background: '#28a745', 
                borderRadius: 4 
              }}>
                {connectedServers} connected
              </span>
            )}
          </div>
          <ul className="conversation-list">
            <li 
              className={`conversation-item ${location.pathname === '/mcp' ? 'active' : ''}`}
              onClick={() => navigate('/mcp')}
            >
              <PlugConnectedRegular />
              <span className="conversation-title">Manage Servers</span>
            </li>
          </ul>
        </div>

        <div style={{ 
          padding: 16, 
          borderTop: '1px solid #3c3c3c',
          marginTop: 'auto'
        }}>
          <Button
            icon={<SettingsRegular />}
            appearance="subtle"
            style={{ width: '100%', justifyContent: 'flex-start' }}
            onClick={() => navigate('/settings')}
          >
            Settings
          </Button>
        </div>
      </aside>

      <main className="main-content">
        <Outlet />
      </main>
    </div>
  )
}
