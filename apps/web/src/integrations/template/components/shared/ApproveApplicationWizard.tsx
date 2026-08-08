import { useEffect, useState } from 'react'
import { LuEye } from 'react-icons/lu'
import { memberApi } from '../../../../core/api/endpoints/memberApi'
import { uploadApi } from '../../../../core/api/endpoints/uploadApi'
import type { Member } from '../../../../core/types/member'
import { describeError } from '../../../../core/utils/apiError'
import { ConfirmationModal } from './ConfirmationModal'
import { FilePreviewModal } from './FilePreviewModal'
import { PipeStepper } from './PipeStepper'
import { StandardButton } from './StandardButton'

const MAX_LENGTH = 32

/** Long enough that a normal typing burst is one request, short enough to feel immediate. */
const CHECK_DEBOUNCE_MS = 350

type AvailabilityStatus = 'idle' | 'checking' | 'available' | 'taken' | 'unknown'

const STEPS = ['RMP Licence', 'Membership ID', 'Confirm']

interface ApproveApplicationWizardProps {
  /** Null closes the wizard; a member opens it. */
  member: Member | null
  /** Called after a successful approval so the caller can refetch. */
  onApproved: () => void | Promise<void>
  onCancel: () => void
}

/**
 * The single path for admitting an application, replacing the old Membership-ID-only dialog.
 *
 * Approving used to be possible while the applicant's RMP licence had never been checked — the
 * licence is the eligibility criterion, and approving issues a control number, generates a receipt
 * and emails the member, so doing it first was backwards. `MemberService.ApproveAsync` now refuses
 * an unverified member outright; this wizard is what stops that gate becoming a round trip between
 * two tabs, by putting the verification decision immediately before the approval it blocks.
 *
 * An already-verified member skips straight to step 2 — re-verifying would write a meaningless
 * extra row into `PrcVerificationHistory`.
 */
export const ApproveApplicationWizard = ({ member, onApproved, onCancel }: ApproveApplicationWizardProps) => {
  const [step, setStep] = useState(0)
  const [verified, setVerified] = useState(false)
  const [membershipNo, setMembershipNo] = useState('')
  const [availability, setAvailability] = useState<AvailabilityStatus>('idle')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [rejecting, setRejecting] = useState(false)
  const [previewing, setPreviewing] = useState(false)

  const isOpen = member !== null
  const memberId = member?.id
  // A member can be listed for verification either because their licence was never reviewed or
  // because they changed it after submitting; the pending value is what's under review when set.
  const licenceUnderReview = member?.pendingPrcLicenseNo ?? member?.prcLicenseNo ?? null

  useEffect(() => {
    if (!member) return
    const alreadyVerified = member.prcIdVerified
    setVerified(alreadyVerified)
    setStep(alreadyVerified ? 1 : 0)
    setMembershipNo('')
    setAvailability('idle')
    setBusy(false)
    setError(null)
    setRejecting(false)
    setPreviewing(false)
  }, [member])

  const trimmed = membershipNo.trim()
  const tooLong = trimmed.length > MAX_LENGTH

  // Same advisory pre-check the standalone dialog had: the approve call re-checks, and the
  // database's case-insensitive unique index is the actual guarantee.
  useEffect(() => {
    if (!isOpen || step !== 1 || !trimmed || tooLong) {
      setAvailability('idle')
      return
    }

    let cancelled = false
    setAvailability('checking')
    const timer = setTimeout(() => {
      memberApi
        .checkMembershipNoAvailability(trimmed, memberId)
        .then((result) => {
          // Discard a response for a value that's since been typed over - debounced requests can
          // resolve out of order.
          if (cancelled || result.membershipNo !== trimmed) return
          setAvailability(result.isAvailable ? 'available' : 'taken')
        })
        .catch(() => {
          if (!cancelled) setAvailability('unknown')
        })
    }, CHECK_DEBOUNCE_MS)

    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [isOpen, step, trimmed, tooLong, memberId])

  useEffect(() => {
    if (!isOpen) return
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !rejecting && !previewing) onCancel()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [isOpen, rejecting, previewing, onCancel])

  if (!member) return null

  const handleVerify = async () => {
    setError(null)
    setBusy(true)
    try {
      await memberApi.approvePrcVerification(member.id)
      setVerified(true)
      setStep(1)
    } catch (err) {
      setError(describeError(err, 'Could not verify this RMP licence. Please try again.'))
    } finally {
      setBusy(false)
    }
  }

  const handleReject = async (reason?: string) => {
    if (!reason) return
    setError(null)
    setBusy(true)
    try {
      await memberApi.rejectPrcVerification(member.id, reason)
      setRejecting(false)
      // Rejecting ends the flow - the application is not approved, and the member stays in the
      // RMP Verification queue with the reason attached.
      await onApproved()
      onCancel()
    } catch (err) {
      setRejecting(false)
      setError(describeError(err, 'Could not reject this RMP licence. Please try again.'))
    } finally {
      setBusy(false)
    }
  }

  const handleApprove = async () => {
    setError(null)
    setBusy(true)
    try {
      await memberApi.approveMember(member.id, trimmed)
      await onApproved()
      onCancel()
    } catch (err) {
      // Stays open on a duplicate Membership ID - the admin has to pick another, and closing
      // would lose what they typed. Sent back to step 2, where the field is.
      setError(describeError(err, 'Could not approve this application. Please try again.'))
      setStep(1)
    } finally {
      setBusy(false)
    }
  }

  const canContinueFromId = Boolean(trimmed) && !tooLong && availability !== 'taken'

  return (
    <div className="fixed inset-0 z-100 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={onCancel} />
      <div className="relative card w-full max-w-lg max-h-full overflow-y-auto">
        <div className="card-header">
          <h6 className="card-title">
            Approve Application - {member.firstName} {member.lastName}
          </h6>
        </div>

        <div className="card-body flex flex-col gap-4">
          <PipeStepper steps={STEPS} step={step} maxStepReached={step} onStepClick={() => {}} navigating />

          {error && <p className="text-sm font-medium text-danger">{error}</p>}

          {step === 0 && (
            <div className="flex flex-col gap-3">
              <p className="text-sm text-default-600">
                Check this licence against the uploaded RMP ID before admitting the application.
              </p>
              <dl className="grid grid-cols-2 gap-3 text-sm">
                <div>
                  <dt className="font-medium text-default-900 mb-1">RMP License No.</dt>
                  <dd className="font-semibold text-default-800">{licenceUnderReview || '-'}</dd>
                </div>
                <div>
                  <dt className="font-medium text-default-900 mb-1">Status</dt>
                  <dd className="font-semibold text-default-800">
                    {member.pendingPrcLicenseNo ? 'Changed after submission' : 'Never reviewed'}
                  </dd>
                </div>
                <div>
                  <dt className="font-medium text-default-900 mb-1">Registration Date</dt>
                  <dd className="font-semibold text-default-800">
                    {member.pendingPrcRegistrationDate ?? member.prcRegistrationDate ?? '-'}
                  </dd>
                </div>
                <div>
                  <dt className="font-medium text-default-900 mb-1">Valid Until</dt>
                  <dd className="font-semibold text-default-800">
                    {member.pendingPrcValidUntilDate ?? member.prcValidUntilDate ?? '-'}
                  </dd>
                </div>
              </dl>
              <div>
                <StandardButton variant="view" size="sm" icon={LuEye} onClick={() => setPreviewing(true)}>
                  View uploaded RMP ID
                </StandardButton>
              </div>
            </div>
          )}

          {step === 1 && (
            <div className="flex flex-col gap-3">
              {verified && <p className="text-sm text-success font-medium">RMP licence verified.</p>}
              <p className="text-sm text-default-600">Enter the PSMPE Membership ID to assign to this member.</p>
              <div>
                <label htmlFor="wizard-membership-no" className="block font-medium text-default-900 text-sm mb-2">
                  Membership ID
                </label>
                <input
                  id="wizard-membership-no"
                  className="form-input"
                  value={membershipNo}
                  maxLength={MAX_LENGTH}
                  aria-invalid={tooLong || availability === 'taken'}
                  aria-describedby="wizard-membership-no-status"
                  onChange={(e) => setMembershipNo(e.target.value)}
                  autoFocus
                />
                <p id="wizard-membership-no-status" aria-live="polite" className="text-xs mt-1 min-h-4">
                  {tooLong ? (
                    <span className="text-danger font-medium">Membership ID must be {MAX_LENGTH} characters or fewer.</span>
                  ) : availability === 'checking' ? (
                    <span className="text-default-500">Checking availability…</span>
                  ) : availability === 'taken' ? (
                    <span className="text-danger font-medium">
                      "{trimmed}" is already assigned to another member. IDs are compared ignoring letter case.
                    </span>
                  ) : availability === 'available' ? (
                    <span className="text-success font-medium">"{trimmed}" is available.</span>
                  ) : availability === 'unknown' ? (
                    <span className="text-default-500">Couldn't check for duplicates - approving will still verify.</span>
                  ) : null}
                </p>
              </div>
            </div>
          )}

          {step === 2 && (
            <div className="flex flex-col gap-3">
              <dl className="grid grid-cols-2 gap-3 text-sm">
                <div>
                  <dt className="font-medium text-default-900 mb-1">Name</dt>
                  <dd className="font-semibold text-default-800">
                    {member.firstName} {member.lastName}
                  </dd>
                </div>
                <div>
                  <dt className="font-medium text-default-900 mb-1">Chapter</dt>
                  <dd className="font-semibold text-default-800">{member.chapter}</dd>
                </div>
                <div>
                  <dt className="font-medium text-default-900 mb-1">RMP License No.</dt>
                  <dd className="font-semibold text-default-800">{licenceUnderReview || '-'}</dd>
                </div>
                <div>
                  <dt className="font-medium text-default-900 mb-1">Membership ID</dt>
                  <dd className="font-semibold text-default-800">{trimmed}</dd>
                </div>
              </dl>
              <p className="text-xs text-default-500">
                Approving assigns this Membership ID, generates the member's receipt and emails it to them. The ID can
                be corrected later on their record, but the approval itself is not reversible here.
              </p>
            </div>
          )}
        </div>

        <div className="card-footer flex items-center justify-between gap-2">
          <StandardButton variant="secondary" onClick={onCancel} disabled={busy}>
            Cancel
          </StandardButton>

          <div className="flex items-center gap-2">
            {step === 0 && (
              <>
                <StandardButton variant="danger" onClick={() => setRejecting(true)} disabled={busy}>
                  Reject
                </StandardButton>
                <StandardButton variant="success" onClick={handleVerify} loading={busy} loadingLabel="Verifying…">
                  Verify
                </StandardButton>
              </>
            )}
            {step === 1 && (
              <>
                {/* Only offered when there was a step 0 to go back to. */}
                {!member.prcIdVerified && (
                  <StandardButton variant="secondary" onClick={() => setStep(0)} disabled={busy}>
                    Back
                  </StandardButton>
                )}
                <StandardButton onClick={() => setStep(2)} disabled={!canContinueFromId || busy}>
                  Continue
                </StandardButton>
              </>
            )}
            {step === 2 && (
              <>
                <StandardButton variant="secondary" onClick={() => setStep(1)} disabled={busy}>
                  Back
                </StandardButton>
                <StandardButton variant="success" onClick={handleApprove} loading={busy} loadingLabel="Approving…">
                  Approve
                </StandardButton>
              </>
            )}
          </div>
        </div>
      </div>

      <ConfirmationModal
        isOpen={rejecting}
        title="Reject RMP verification"
        message="This will discard the pending RMP change and notify the member with your reason. The application will not be approved."
        confirmLabel="Reject"
        confirmVariant="danger"
        reasonRequired
        onConfirm={handleReject}
        onCancel={() => setRejecting(false)}
      />

      {previewing && (
        <FilePreviewModal
          isOpen
          title="RMP ID Document"
          fetchFile={() => uploadApi.fetchMemberPrcIdUrl(member.id)}
          onClose={() => setPreviewing(false)}
        />
      )}
    </div>
  )
}
