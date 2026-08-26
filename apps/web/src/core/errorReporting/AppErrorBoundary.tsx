import { Component, type ErrorInfo, type ReactNode } from 'react'
import { reportError } from './reportError'

interface AppErrorBoundaryProps {
  children: ReactNode
}

interface AppErrorBoundaryState {
  hasError: boolean
}

export class AppErrorBoundary extends Component<AppErrorBoundaryProps, AppErrorBoundaryState> {
  state: AppErrorBoundaryState = { hasError: false }

  static getDerivedStateFromError(): AppErrorBoundaryState {
    return { hasError: true }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    reportError({ message: error.message, stackTrace: error.stack, componentStack: errorInfo.componentStack ?? undefined })
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="flex items-center justify-center min-h-screen p-6 text-center">
          <div>
            <h1 className="text-xl font-semibold text-default-900 mb-2">Something went wrong</h1>
            <p className="text-sm text-default-600 mb-4">
              The page ran into an unexpected error. Try reloading.
            </p>
            <button type="button" className="btn btn-sm bg-primary text-white" onClick={() => window.location.reload()}>
              Reload
            </button>
          </div>
        </div>
      )
    }

    return this.props.children
  }
}
