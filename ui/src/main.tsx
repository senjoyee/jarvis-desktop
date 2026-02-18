import React from 'react'
import ReactDOM from 'react-dom/client'
import { FluentProvider, webDarkTheme, type Theme } from '@fluentui/react-components'
import { HashRouter } from 'react-router-dom'
import App from './App'
import './index.css'

// Override Fluent UI's default Segoe UI fonts with our design system fonts
const customTheme: Theme = {
  ...webDarkTheme,
  fontFamilyBase: "'Söhne', 'ui-sans-serif', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif",
  fontFamilyMonospace: "'JetBrains Mono', 'Fira Code', monospace",
  fontFamilyNumeric: "'Söhne', 'ui-sans-serif', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif",
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <FluentProvider theme={customTheme}>
      <HashRouter>
        <App />
      </HashRouter>
    </FluentProvider>
  </React.StrictMode>,
)

