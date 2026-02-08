import { useState, useEffect } from 'react'
import { Button, Input, Spinner } from '@fluentui/react-components'
import { CheckmarkRegular, DismissRegular, OpenRegular } from '@fluentui/react-icons'
import { useStore } from '../store'

export default function SettingsPage() {
  const [apiKey, setApiKey] = useState('')
  const [isTesting, setIsTesting] = useState(false)
  const [testResult, setTestResult] = useState<boolean | null>(null)

  const hasApiKey = useStore((state) => state.hasApiKey)
  const checkApiKey = useStore((state) => state.checkApiKey)
  const setStoreApiKey = useStore((state) => state.setApiKey)
  const clearApiKey = useStore((state) => state.clearApiKey)
  const testOpenRouter = useStore((state) => state.testOpenRouter)
  const availableModels = useStore((state) => state.availableModels)
  const loadModels = useStore((state) => state.loadModels)

  useEffect(() => {
    checkApiKey()
    loadModels()
  }, [checkApiKey, loadModels])

  const handleSaveKey = async () => {
    if (!apiKey.trim()) return
    await setStoreApiKey(apiKey.trim())
    setApiKey('')
    setTestResult(null)
  }

  const handleClearKey = async () => {
    await clearApiKey()
    setTestResult(null)
  }

  const handleTestConnection = async () => {
    setIsTesting(true)
    setTestResult(null)
    try {
      const result = await testOpenRouter()
      setTestResult(result)
    } catch {
      setTestResult(false)
    }
    setIsTesting(false)
  }

  // Get unique providers from models
  const providers = [...new Set(availableModels.map(m => m.provider))].sort()

  return (
    <div className="settings-page">
      <h1 style={{ marginBottom: 32 }}>Settings</h1>

      <div className="settings-section">
        <h3>OpenRouter API Key</h3>
        <p style={{ color: '#888', marginBottom: 8, fontSize: 14 }}>
          OpenRouter provides unified access to AI models from multiple providers.
        </p>
        <p style={{ color: '#888', marginBottom: 16, fontSize: 14 }}>
          <a
            href="https://openrouter.ai/keys"
            target="_blank"
            rel="noopener noreferrer"
            style={{ color: '#60a5fa', textDecoration: 'none' }}
          >
            Get your API key from openrouter.ai <OpenRegular style={{ verticalAlign: 'middle' }} />
          </a>
        </p>

        {hasApiKey ? (
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 16 }}>
              <span style={{ color: '#28a745' }}>✓ OpenRouter API key is configured</span>
              <Button appearance="secondary" onClick={handleClearKey}>
                Clear Key
              </Button>
            </div>

            <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
              <Button
                appearance="primary"
                onClick={handleTestConnection}
                disabled={isTesting}
              >
                {isTesting ? <Spinner size="tiny" /> : 'Test Connection'}
              </Button>
              {testResult !== null && (
                <span style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 4,
                  color: testResult ? '#28a745' : '#dc3545'
                }}>
                  {testResult ? (
                    <>
                      <CheckmarkRegular /> Connection successful
                    </>
                  ) : (
                    <>
                      <DismissRegular /> Connection failed
                    </>
                  )}
                </span>
              )}
            </div>
          </div>
        ) : (
          <div>
            <div className="form-group">
              <label>Enter your OpenRouter API key</label>
              <Input
                type="password"
                value={apiKey}
                onChange={(_, data) => setApiKey(data.value)}
                placeholder="sk-or-..."
                style={{ width: '100%', maxWidth: 400 }}
              />
            </div>
            <Button
              appearance="primary"
              onClick={handleSaveKey}
              disabled={!apiKey.trim()}
            >
              Save API Key
            </Button>
          </div>
        )}
      </div>

      <div className="settings-section">
        <h3>Available AI Providers</h3>
        <p style={{ color: '#888', marginBottom: 16, fontSize: 14 }}>
          With OpenRouter, you have access to {availableModels.length} models from {providers.length} providers:
        </p>
        <div style={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 8,
          marginBottom: 8
        }}>
          {providers.map(provider => {
            const count = availableModels.filter(m => m.provider === provider).length
            return (
              <span
                key={provider}
                style={{
                  padding: '4px 12px',
                  background: 'rgba(255,255,255,0.1)',
                  borderRadius: 16,
                  fontSize: 13
                }}
              >
                {provider} ({count})
              </span>
            )
          })}
        </div>
        <p style={{ color: '#666', fontSize: 12, marginTop: 8 }}>
          🧠 = Supports reasoning/thinking mode
        </p>
      </div>

      <div className="settings-section">
        <h3>Data Storage</h3>
        <p style={{ color: '#888', fontSize: 14 }}>
          Conversations and settings are stored locally in:
        </p>
        <code style={{
          display: 'block',
          marginTop: 8,
          padding: 12,
          background: '#1e1e1e',
          borderRadius: 4,
          fontSize: 13
        }}>
          %LOCALAPPDATA%\JarvisDesktop
        </code>
      </div>

      <div className="settings-section">
        <h3>About</h3>
        <p style={{ color: '#888', fontSize: 14 }}>
          Jarvis Desktop v1.1.0
        </p>
        <p style={{ color: '#888', fontSize: 14, marginTop: 8 }}>
          A Windows desktop AI chat client with MCP support and multi-provider models via OpenRouter.
        </p>
      </div>
    </div>
  )
}
