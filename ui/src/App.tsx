import { Routes, Route } from 'react-router-dom'
import { useEffect } from 'react'
import Layout from './components/Layout'
import ChatPage from './pages/ChatPage'
import SettingsPage from './pages/SettingsPage'
import McpPage from './pages/McpPage'
import { useStore } from './store'

function App() {
  const loadConversations = useStore((state) => state.loadConversations)
  const loadMcpServers = useStore((state) => state.loadMcpServers)

  useEffect(() => {
    loadConversations()
    loadMcpServers()
  }, [loadConversations, loadMcpServers])

  return (
    <Routes>
      <Route path="/" element={<Layout />}>
        <Route index element={<ChatPage />} />
        <Route path="chat/:conversationId?" element={<ChatPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="mcp" element={<McpPage />} />
      </Route>
    </Routes>
  )
}

export default App
