import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@fontsource/inter/400.css'
import '@fontsource/inter/500.css'
import '@fontsource/inter/600.css'
import './ui/tokens.scss'
import './index.scss'
import App from './App.tsx'
import { BrowserRouter } from 'react-router'
import { ErrorBoundary } from './app/ErrorBoundary'
import { ThemeProvider } from './ui/theme/ThemeProvider'
import { applyInitialTheme } from './ui/theme/theme'

applyInitialTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ErrorBoundary>
      <ThemeProvider>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </ThemeProvider>
    </ErrorBoundary>
  </StrictMode>,
)
