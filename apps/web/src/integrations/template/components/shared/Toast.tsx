import { useEffect } from 'react'
import { LuCheck, LuTriangleAlert, LuX } from 'react-icons/lu'

export interface ToastStatus {
  ok: boolean
  text: string
}

interface ToastProps {
  /** Null hides the toast. A new object - even with the same text - shows it afresh and restarts
   *  the timer, so sending a second email still pops a second toast. */
  status: ToastStatus | null
  onDismiss: () => void
}

/** A confirmation reads and is gone; a failure stays long enough to read what went wrong. */
const SUCCESS_MS = 4000
const ERROR_MS = 8000

/**
 * Custom-built (no toast library, matching ConfirmationModal) corner notification for the result
 * of an action - "Verification email sent.", "Could not send...". Sits above the modal overlay
 * (z-110 vs its z-100) so a result isn't hidden behind a dialog, dismisses itself, and can be
 * closed early. Success is announced politely (role=status); a failure is announced assertively
 * (role=alert) because the user needs to know it didn't work.
 */
export const Toast = ({ status, onDismiss }: ToastProps) => {
  useEffect(() => {
    if (!status) return
    const timer = setTimeout(onDismiss, status.ok ? SUCCESS_MS : ERROR_MS)
    return () => clearTimeout(timer)
    // onDismiss is intentionally not a dependency: a parent passing a fresh arrow each render
    // would otherwise restart the timer on every render and the toast would never go away.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status])

  if (!status) return null

  return (
    <div className="fixed top-20 right-5 z-110 w-full max-w-sm px-4 sm:px-0 pointer-events-none">
      <div
        role={status.ok ? 'status' : 'alert'}
        className={`pointer-events-auto flex items-start gap-3 rounded-lg p-4 shadow-lg text-white ${
          status.ok ? 'bg-success' : 'bg-danger'
        }`}
      >
        {status.ok ? <LuCheck className="size-5 shrink-0 mt-0.5" /> : <LuTriangleAlert className="size-5 shrink-0 mt-0.5" />}
        <p className="flex-1 text-sm font-medium">{status.text}</p>
        <button
          type="button"
          onClick={onDismiss}
          className="shrink-0 rounded p-0.5 text-white/80 hover:text-white hover:bg-white/15"
          aria-label="Dismiss"
        >
          <LuX className="size-4" />
        </button>
      </div>
    </div>
  )
}
