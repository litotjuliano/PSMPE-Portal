import { reportError } from './reportError'

/** Catches errors React's own boundary can't - runtime errors outside render (event handlers,
 *  timers) and unhandled promise rejections. Call once, at app bootstrap. */
export function setupGlobalErrorHandlers() {
  window.addEventListener('error', (event) => {
    reportError({ message: event.message, stackTrace: event.error?.stack })
  })

  window.addEventListener('unhandledrejection', (event) => {
    const reason = event.reason
    reportError({
      message: reason instanceof Error ? reason.message : String(reason),
      stackTrace: reason instanceof Error ? reason.stack : undefined,
    })
  })
}
