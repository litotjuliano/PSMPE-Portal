import { API_BASE_URL, tokenStorage } from '../api/apiClient'

interface FrontendErrorReport {
  message: string
  stackTrace?: string
  url?: string
  componentStack?: string
}

// Uses plain `fetch`, not the shared `apiClient`/axios instance - an error captured while axios
// itself is misbehaving must not recurse back through axios's own interceptor stack (see
// apiClient.ts's 401 interceptor, which clears the session and redirects on any 401 - we do NOT
// want a failed error-report POST triggering that). Swallows its own failures; this is the last
// line of defense and must never throw.
export function reportError(report: FrontendErrorReport) {
  try {
    const token = tokenStorage.get()
    fetch(`${API_BASE_URL}/api/errors/frontend`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify({
        message: report.message,
        stackTrace: report.stackTrace ?? null,
        url: report.url ?? window.location.href,
        componentStack: report.componentStack ?? null,
      }),
    }).catch(() => {
      // Best-effort - if the report itself fails to send, there is nowhere left to report that.
    })
  } catch {
    // Same reasoning as the .catch() above, but for synchronous failures (e.g. localStorage
    // access denied in a sandboxed context, or fetch() throwing synchronously on construction) -
    // this function is the last line of defense and must never throw back into its callers
    // (AppErrorBoundary.componentDidCatch, the global error/unhandledrejection listeners).
  }
}
