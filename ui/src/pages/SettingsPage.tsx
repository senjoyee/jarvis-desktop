import { useState, useEffect } from 'react'
import { Button, Input, Spinner } from '@fluentui/react-components'
import { CheckmarkRegular, DismissRegular } from '@fluentui/react-icons'
import { useStore } from '../store'

export default function SettingsPage() {
  const [apiKey, setApiKey] = useState('')
  const [isTesting, setIsTesting] = useState(false)
  const [testResult, setTestResult] = useState<boolean | null>(null)

  const hasApiKey = useStore((state) => state.hasApiKey)
  const checkApiKey = useStore((state) => state.checkApiKey)
  const setStoreApiKey = useStore((state) => state.setApiKey)
  const clearApiKey = useStore((state) => state.clearApiKey)
  const testOpenAI = useStore((state) => state.testOpenAI)

  useEffect(() => {
    checkApiKey()
  }, [checkApiKey])

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
      const result = await testOpenAI()
      setTestResult(result)
    } catch {
      setTestResult(false)
    }
    setIsTesting(false)
  }

  return (
    <div className="settings-page">
      <h1 style={{ marginBottom: 32 }}>Settings</h1>

      <div className="settings-section">
        <h3>OpenAI API Key</h3>
        <p style={{ color: '#888', marginBottom: 16, fontSize: 14 }}>
          Your API key is stored securely in Windows Credential Manager.
        </p>

        {hasApiKey ? (
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 16 }}>
              <span style={{ color: '#28a745' }}>✓ API key is configured</span>
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
              <label>Enter your OpenAI API key</label>
              <Input
                type="password"
                value={apiKey}
                onChange={(_, data) => setApiKey(data.value)}
                placeholder="sk-..."
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
          %LOCALAPPDATA%\ChloyeDesktop
        </code>
      </div>

      <div className="settings-section">
        <h3>About</h3>
        <p style={{ color: '#888', fontSize: 14 }}>
          Chloye Desktop v1.0.0
        </p>
        <p style={{ color: '#888', fontSize: 14, marginTop: 8 }}>
          A Windows desktop chat client with MCP support.
        </p>
      </div>
    </div>
  )
}
