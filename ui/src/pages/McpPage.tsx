import { useState, useEffect } from 'react'
import { Button, Spinner, Textarea, Switch } from '@fluentui/react-components'
import {
  ArrowSyncRegular,
  FolderOpenRegular
} from '@fluentui/react-icons'
import { useStore } from '../store'
import type { McpServer, McpTool } from '../types'
import { invoke } from '../services/bridge'

export default function McpPage() {
  const [selectedServer, setSelectedServer] = useState<McpServer | null>(null)
  const [serverTools, setServerTools] = useState<McpTool[]>([])
  const [serverLogs, setServerLogs] = useState<string[]>([])
  const [isLoadingTools, setIsLoadingTools] = useState(false)
  const [configPath, setConfigPath] = useState<string>('')

  const mcpServers = useStore((state) => state.mcpServers)
  const loadMcpServers = useStore((state) => state.loadMcpServers)
  const startMcpServer = useStore((state) => state.startMcpServer)
  const stopMcpServer = useStore((state) => state.stopMcpServer)
  const getMcpTools = useStore((state) => state.getMcpTools)
  const getMcpLogs = useStore((state) => state.getMcpLogs)

  useEffect(() => {
    loadMcpServers()
    loadConfigPath()
  }, [loadMcpServers])

  const loadConfigPath = async () => {
    try {
      const path = await invoke('mcp.configPath')
      setConfigPath(path as string)
    } catch (err) {
      console.error('Failed to get config path:', err)
    }
  }

  const handleOpenConfig = async () => {
    try {
      await invoke('mcp.openConfig')
      // Reload servers after a short delay to pick up changes
      setTimeout(() => loadMcpServers(), 1000)
    } catch (err) {
      console.error('Failed to open config:', err)
    }
  }

  useEffect(() => {
    if (selectedServer && selectedServer.status === 'connected') {
      loadServerDetails(selectedServer.id)
    }
  }, [selectedServer?.id, selectedServer?.status])

  const loadServerDetails = async (serverId: string) => {
    setIsLoadingTools(true)
    try {
      const [tools, logs] = await Promise.all([
        getMcpTools(serverId),
        getMcpLogs(serverId)
      ])
      setServerTools(tools)
      setServerLogs(logs)
    } catch (err) {
      console.error('Failed to load server details:', err)
    }
    setIsLoadingTools(false)
  }

  const handleStart = async (server: McpServer) => {
    await startMcpServer(server.id)
    await loadMcpServers()
  }

  const handleStop = async (server: McpServer) => {
    await stopMcpServer(server.id)
    await loadMcpServers()
  }

  return (
    <div className="mcp-page">
      <h1 style={{ marginBottom: 24 }}>MCP Servers</h1>

      <div style={{ display: 'flex', gap: 24 }}>
        <div style={{ flex: 1 }}>
          <div className="config-section">
            <h3 style={{ marginBottom: 12 }}>Configuration</h3>
            <p style={{ color: '#888', fontSize: 13, marginBottom: 12 }}>
              Edit the config file to add or remove MCP servers. After saving, click Reload to apply changes.
            </p>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 8 }}>
              <code style={{
                flex: 1,
                padding: '8px 12px',
                background: '#1e1e1e',
                borderRadius: 4,
                fontSize: 12,
                color: '#888'
              }}>
                {configPath || 'Loading...'}
              </code>
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              <Button
                icon={<FolderOpenRegular />}
                appearance="primary"
                onClick={handleOpenConfig}
              >
                Open Config File
              </Button>
              <Button
                icon={<ArrowSyncRegular />}
                appearance="secondary"
                onClick={() => loadMcpServers()}
              >
                Reload
              </Button>
            </div>
          </div>

          <h3 style={{ marginTop: 32, marginBottom: 12 }}>Configured Servers ({mcpServers.length})</h3>
          {mcpServers.length === 0 ? (
            <p style={{ color: '#888' }}>No servers configured. Click "Open Config File" to add servers.</p>
          ) : (
            mcpServers.map((server) => (
              <div
                key={server.id}
                className={`server-card ${selectedServer?.id === server.id ? 'active' : ''}`}
                onClick={() => setSelectedServer(server)}
                style={{
                  cursor: 'pointer',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  padding: '12px 16px',
                  gap: 12
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 12, overflow: 'hidden' }}>
                  <div style={{
                    width: 8,
                    height: 8,
                    borderRadius: '50%',
                    background: server.status === 'connected' ? '#28a745' :
                      server.status === 'connecting' ? '#ffc107' :
                        server.status === 'error' ? '#dc3545' : '#666',
                    flexShrink: 0
                  }} />
                  <span className="server-name" style={{ fontWeight: 600 }}>{server.name}</span>
                </div>

                <div onClick={(e) => e.stopPropagation()}>
                  <Switch
                    checked={server.status === 'connected' || server.status === 'connecting'}
                    onChange={(_, data) => {
                      if (data.checked) {
                        handleStart(server)
                      } else {
                        handleStop(server)
                      }
                    }}
                    disabled={server.status === 'connecting'}
                  />
                </div>
              </div>
            ))
          )}
        </div>

        {selectedServer && (
          <div style={{ flex: 1, maxWidth: 500 }}>
            <ServerDetails
              server={selectedServer}
              tools={serverTools}
              logs={serverLogs}
              isLoading={isLoadingTools}
              onRefresh={() => loadServerDetails(selectedServer.id)}
            />
          </div>
        )}
      </div>
    </div>
  )
}

function ServerDetails({
  server,
  tools,
  logs,
  isLoading,
  onRefresh
}: {
  server: McpServer
  tools: McpTool[]
  logs: string[]
  isLoading: boolean
  onRefresh: () => void
}) {
  const [showToolRunner, setShowToolRunner] = useState(false)
  const [selectedTool, setSelectedTool] = useState<McpTool | null>(null)

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <h3>{server.name} Details</h3>
        <Button
          icon={<ArrowSyncRegular />}
          appearance="subtle"
          size="small"
          onClick={onRefresh}
          disabled={server.status !== 'connected'}
        />
      </div>

      <div style={{ marginBottom: 20 }}>
        <div style={{
          fontSize: 12,
          color: '#888',
          fontFamily: 'var(--font-mono)',
          background: '#1e1e1e',
          padding: '8px 12px',
          borderRadius: 4,
          wordBreak: 'break-all'
        }}>
          {server.type === 'local' ? (
            <span>Command: {server.command}</span>
          ) : (
            <span>URL: {server.url}</span>
          )}
        </div>
      </div>

      {server.status !== 'connected' ? (
        <p style={{ color: '#888' }}>Start the server to view tools and logs.</p>
      ) : isLoading ? (
        <Spinner size="small" label="Loading..." />
      ) : (
        <>
          <div className="tool-list">
            <h4 style={{ marginBottom: 12 }}>Tools ({tools.length})</h4>
            {tools.length === 0 ? (
              <p style={{ color: '#888', fontSize: 13 }}>No tools available</p>
            ) : (
              tools.map((tool) => (
                <div
                  key={tool.name}
                  className="tool-item"
                  onClick={() => {
                    setSelectedTool(tool)
                    setShowToolRunner(true)
                  }}
                  style={{ cursor: 'pointer' }}
                >
                  <div className="tool-name">{tool.name}</div>
                  {tool.description && (
                    <div className="tool-description">{tool.description}</div>
                  )}
                </div>
              ))
            )}
          </div>

          <div style={{ marginTop: 20 }}>
            <h4 style={{ marginBottom: 12 }}>Logs</h4>
            <div className="logs-panel">
              {logs.length === 0 ? (
                <span style={{ color: '#888' }}>No logs yet</span>
              ) : (
                logs.map((line, i) => (
                  <div key={i} className="log-line">{line}</div>
                ))
              )}
            </div>
          </div>
        </>
      )}

      {showToolRunner && selectedTool && (
        <ToolRunnerModal
          serverId={server.id}
          tool={selectedTool}
          onClose={() => {
            setShowToolRunner(false)
            setSelectedTool(null)
          }}
        />
      )}
    </div>
  )
}

function ToolRunnerModal({
  serverId,
  tool,
  onClose
}: {
  serverId: string
  tool: McpTool
  onClose: () => void
}) {
  const [argsJson, setArgsJson] = useState('{}')
  const [result, setResult] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [isRunning, setIsRunning] = useState(false)

  const callMcpTool = useStore((state) => state.callMcpTool)

  const handleRun = async () => {
    setIsRunning(true)
    setResult(null)
    setError(null)

    try {
      const args = JSON.parse(argsJson)
      const response = await callMcpTool(serverId, tool.name, args)
      setResult(JSON.stringify(response, null, 2))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error')
    }
    setIsRunning(false)
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()} style={{ width: 600 }}>
        <h2>Run Tool: {tool.name}</h2>

        {tool.description && (
          <p style={{ color: '#888', marginBottom: 16 }}>{tool.description}</p>
        )}

        <div className="form-group">
          <label>Arguments (JSON)</label>
          <Textarea
            value={argsJson}
            onChange={(_, data) => setArgsJson(data.value)}
            style={{ fontFamily: 'var(--font-mono)', minHeight: 100 }}
          />
        </div>

        {tool.inputSchema && (
          <details style={{ marginBottom: 16 }}>
            <summary style={{ cursor: 'pointer', color: '#888' }}>Input Schema</summary>
            <pre style={{
              background: '#1e1e1e',
              padding: 12,
              borderRadius: 4,
              fontSize: 12,
              overflow: 'auto',
              maxHeight: 200
            }}>
              {JSON.stringify(tool.inputSchema, null, 2)}
            </pre>
          </details>
        )}

        <Button
          appearance="primary"
          onClick={handleRun}
          disabled={isRunning}
          style={{ marginBottom: 16 }}
        >
          {isRunning ? <Spinner size="tiny" /> : 'Run Tool'}
        </Button>

        {error && (
          <div style={{
            padding: 12,
            background: '#5c1e1e',
            borderRadius: 4,
            marginBottom: 16,
            color: '#ff8080'
          }}>
            Error: {error}
          </div>
        )}

        {result && (
          <div>
            <label style={{ display: 'block', marginBottom: 8, color: '#888' }}>Result</label>
            <pre style={{
              background: '#1e1e1e',
              padding: 12,
              borderRadius: 4,
              fontSize: 12,
              overflow: 'auto',
              maxHeight: 300
            }}>
              {result}
            </pre>
          </div>
        )}

        <div className="modal-actions">
          <Button appearance="secondary" onClick={onClose}>
            Close
          </Button>
        </div>
      </div>
    </div>
  )
}
