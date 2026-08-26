import { useEffect } from 'react'
import { StandardButton } from './StandardButton'

interface LogDetailsModalProps {
  isOpen: boolean
  title: string
  content: string | null
  onClose: () => void
}

/**
 * Follows ConfirmationModal's shell (backdrop, Escape-to-close, card/card-header/card-body) but
 * shows read-only JSON/text instead of a confirmation prompt - used for audit and error log entry
 * details.
 */
export const LogDetailsModal = ({ isOpen, title, content, onClose }: LogDetailsModalProps) => {
  useEffect(() => {
    if (!isOpen) return
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [isOpen, onClose])

  if (!isOpen) return null

  return (
    <div className="fixed inset-0 z-100 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />
      <div className="relative card w-full max-w-2xl">
        <div className="card-header">
          <h6 className="card-title">{title}</h6>
        </div>
        <div className="card-body">
          <pre className="text-xs whitespace-pre-wrap break-words max-h-96 overflow-y-auto bg-default-50 p-3 rounded">
            {content ?? '(no details)'}
          </pre>
        </div>
        <div className="card-footer flex items-center justify-end">
          <StandardButton variant="secondary" onClick={onClose}>
            Close
          </StandardButton>
        </div>
      </div>
    </div>
  )
}
