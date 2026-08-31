import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/index.css'
import { App } from './App.tsx'
import { setupGlobalErrorHandlers } from './core/errorReporting/setupGlobalErrorHandlers'

setupGlobalErrorHandlers()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
