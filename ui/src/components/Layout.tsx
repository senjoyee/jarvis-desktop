import { Outlet, useNavigate, useLocation } from 'react-router-dom'
import { Button } from '@fluentui/react-components'
import {
  ChatRegular,
  SettingsRegular,
  PlugConnectedRegular,
  AddRegular,
  DeleteRegular,
  NavigationRegular
} from '@fluentui/react-icons'
import { useStore } from '../store'
import type { Conversation } from '../types'
import { useState } from 'react'

export default function Layout() {
  const navigate = useNavigate()
  const location = useLocation()
  const [collapsed, setCollapsed] = useState(false)

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

  const sortedConversations = [...conversations].sort((a, b) => {
    if (a.isPinned === b.isPinned) return 0
    return a.isPinned ? -1 : 1
  })

  return (
    <div className="app-layout">
      <aside className={`sidebar ${collapsed ? 'collapsed' : ''}`}>
        <div className="sidebar-header">
          <Button
            icon={<NavigationRegular />}
            appearance="subtle"
            onClick={() => setCollapsed(!collapsed)}
            title="Toggle Sidebar"
          />
          {!collapsed && <span style={{ fontWeight: 600, fontSize: 16 }}>Jarvis</span>}
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
            {sortedConversations.map((conv) => (
              <li
                key={conv.id}
                className={`conversation-item ${currentConversationId === conv.id ? 'active' : ''}`}
                onClick={() => handleSelectConversation(conv)}
                title={collapsed ? conv.title : undefined}
              >
                <ChatRegular />
                <span className="conversation-title">
                  {conv.title}
                  {conv.isPinned && <span style={{ marginLeft: 6, fontSize: 10 }}>⭐</span>}
                </span>
                <Button
                  className="delete-btn"
                  icon={<DeleteRegular />}
                  appearance="subtle"
                  size="small"
                  onClick={(e) => handleDeleteConversation(e, conv.id)}
                  style={{ opacity: 0.5 }}
                />
              </li>
            ))}
            {conversations.length === 0 && (
              <li className="no-conversations" style={{ padding: '10px 16px', color: '#888', fontSize: 13 }}>
                No conversations
              </li>
            )}
          </ul>

          <div className="sidebar-section" style={{ marginTop: 24 }}>
            <span>MCP Servers</span>
            {connectedServers > 0 && (
              <span className="server-badge" style={{
                marginLeft: 8,
                fontSize: 10,
                padding: '2px 6px',
                background: '#28a745',
                borderRadius: 4
              }}>
                {connectedServers}
              </span>
            )}
          </div>
          <ul className="conversation-list">
            <li
              className={`conversation-item ${location.pathname === '/mcp' ? 'active' : ''}`}
              onClick={() => navigate('/mcp')}
              title={collapsed ? "Manage Servers" : undefined}
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
            style={{ width: '100%', justifyContent: collapsed ? 'center' : 'flex-start' }}
            onClick={() => navigate('/settings')}
            title={collapsed ? "Settings" : undefined}
          >
            {!collapsed && "Settings"}
          </Button>
        </div>
      </aside>

      <main className="main-content">
        <Outlet />
      </main>
    </div>
  )
}
