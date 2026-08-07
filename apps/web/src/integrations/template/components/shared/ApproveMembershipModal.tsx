import { useEffect, useState } from 'react'
import { StandardButton } from './StandardButton'

interface ApproveMembershipModalProps {
  isOpen: boolean
  /** Shown so the admin can confirm which application they're numbering. */
  memberName?: string
  /** Rejects to keep the dialog open with an error - resolves to close it. */
  onConfirm: (membershipNo: string) => Promise<void>
  onCancel: () => void
}

/**
 * Collects PSMPE's membership control number, which is mandatory to approve an application - the
 * portal never generates one.
 *
 * Not built on ConfirmationModal: that renders a textarea for its reason mode, and its onConfirm
 * has no error path (it clears `submitting` in a finally while the caller closes the dialog). This
 * flow has to survive a 409 from a duplicate number, which means staying open and showing why.
 */
export const ApproveMembershipModal = ({ isOpen, memberName, onConfirm, onCancel }: ApproveMembershipModalProps) => {
  const [membershipNo, setMembershipNo] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (isOpen) {
      setMembershipNo('')
      setSubmitting(false)
      setError(null)
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

  const trimmed = membershipNo.trim()

  const handleConfirm = async () => {
    if (!trimmed) return
    setSubmitting(true)
    setError(null)
    try {
      await onConfirm(trimmed)
    } catch (err) {
      // Deliberately leaves the dialog open - the admin needs to pick a different number, and
      // closing would lose what they typed.
      setError(err instanceof Error ? err.message : 'Could not approve this application. Please try again.')
      setSubmitting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-100 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={onCancel} />
      <div className="relative card w-full max-w-md">
        <div className="card-header">
          <h6 className="card-title">Approve Application</h6>
        </div>
        <div className="card-body flex flex-col gap-3">
          <p className="text-sm text-default-600">
            {memberName
              ? `Enter the PSMPE Membership ID to assign to ${memberName}.`
              : 'Enter the PSMPE Membership ID to assign to this member.'}
          </p>
          <div>
            <label htmlFor="approve-membership-no" className="block font-medium text-default-900 text-sm mb-2">
              Membership ID
            </label>
            <input
              id="approve-membership-no"
              className="form-input"
              value={membershipNo}
              maxLength={32}
              onChange={(e) => setMembershipNo(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') handleConfirm()
              }}
              autoFocus
            />
          </div>
          {error && <p className="text-sm font-medium text-danger">{error}</p>}
          <p className="text-xs text-default-500">
            This is printed on the member's receipt and included in their approval email. It can be corrected later on
            the member's record.
          </p>
        </div>
        <div className="card-footer flex items-center justify-end gap-2">
          <StandardButton variant="secondary" onClick={onCancel} disabled={submitting}>
            Cancel
          </StandardButton>
          <StandardButton
            variant="success"
            onClick={handleConfirm}
            disabled={!trimmed}
            loading={submitting}
            loadingLabel="Approving…"
          >
            Approve
          </StandardButton>
        </div>
      </div>
    </div>
  )
}
