import { useEffect, useState } from 'react'
import { StandardButton, type StandardButtonVariant } from './StandardButton'

interface ConfirmationModalProps {
  isOpen: boolean
  title: string
  message?: string
  confirmLabel?: string
  confirmVariant?: StandardButtonVariant
  /** When true, renders a required reason textarea and disables Confirm until it's non-empty -
   *  same UX PrcVerificationsTable's inline reject flow used before this component existed. */
  reasonRequired?: boolean
  onConfirm: (reason?: string) => void | Promise<void>
  onCancel: () => void
}

/**
 * Custom-built (no Preline dependency) centered overlay + backdrop - closes on backdrop click or
 * Escape. Used for delete confirmations and reason-required decisions (Reject, Request Additional
 * Documents) across every CRUD module.
 */
export const ConfirmationModal = ({
  isOpen,
  title,
  message,
  confirmLabel = 'Confirm',
  confirmVariant = 'danger',
  reasonRequired = false,
  onConfirm,
  onCancel,
}: ConfirmationModalProps) => {
  const [reason, setReason] = useState('')
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    if (isOpen) {
      setReason('')
      setSubmitting(false)
    }
  }, [isOpen])

  useEffect(() => {
    if (!isOpen) return
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [isOpen, onCancel])

  if (!isOpen) return null

  const blockedByMissingReason = reasonRequired && !reason.trim()

  const handleConfirm = async () => {
    if (blockedByMissingReason) return
    setSubmitting(true)
    try {
      await onConfirm(reasonRequired ? reason.trim() : undefined)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-100 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={onCancel} />
      <div className="relative card w-full max-w-md">
        <div className="card-header">
          <h6 className="card-title">{title}</h6>
        </div>
        <div className="card-body flex flex-col gap-3">
          {message && <p className="text-sm text-default-600">{message}</p>}
          {reasonRequired && (
            <textarea
              className="form-input text-sm"
              rows={3}
              placeholder="Reason…"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              autoFocus
            />
          )}
        </div>
        <div className="card-footer flex items-center justify-end gap-2">
          <StandardButton variant="secondary" onClick={onCancel} disabled={submitting}>
            Cancel
          </StandardButton>
          <StandardButton
            variant={confirmVariant}
            onClick={handleConfirm}
            disabled={blockedByMissingReason}
            loading={submitting}
            loadingLabel="Submitting…"
          >
            {confirmLabel}
          </StandardButton>
        </div>
      </div>
    </div>
  )
}
