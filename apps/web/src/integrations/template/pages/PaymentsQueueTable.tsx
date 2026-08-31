import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { LuCheck, LuEye, LuTriangleAlert, LuX } from 'react-icons/lu'
import { paymentApi, type MembershipFees, type Payment } from '../../../core/api/endpoints/paymentApi'
import { ConfirmationModal } from '../components/shared/ConfirmationModal'
import { FilePreviewModal } from '../components/shared/FilePreviewModal'
import { StandardButton } from '../components/shared/StandardButton'

interface PaymentsQueueTableProps {
  payments: Payment[]
  /** Gates Verify/Reject - false for an Approval user, who can view this queue but not act on
   *  it (verify/reject are Members.Manage-gated server side). */
  canManagePayments: boolean
  onVerify: (id: string) => void
  onReject: (id: string, reason: string) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

const KIND_LABELS: Record<Payment['kind'], string> = {
  NewMembership: 'New membership',
  Renewal: 'Renewal',
  EventRegistration: 'Event registration',
  PortalAccessOnly: 'Portal access',
}

const PROOF_MISSING_MESSAGE =
  "This file was recorded but could not be found in storage. It may have been lost — ask the member to resubmit their proof."

const peso = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })

/**
 * The admin payment queue. Unlike the other three Members tabs this lists *payments*, not members,
 * so it has its own fetch and its own table - see openspecs/payments.md for why it still lives as a
 * tab rather than a fourth nav entry.
 */
export const PaymentsQueueTable = ({
  payments,
  canManagePayments,
  onVerify,
  onReject,
  page,
  pageSize,
  totalCount,
  onPageChange,
}: PaymentsQueueTableProps) => {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const [rejectingId, setRejectingId] = useState<string | null>(null)
  const [previewingId, setPreviewingId] = useState<string | null>(null)
  // Own fetch, same self-containment as this component's payments prop - fees rarely change and
  // this is a read-only admin view, so one fetch on mount is enough (no per-row or per-render
  // refetching).
  const [fees, setFees] = useState<MembershipFees | null>(null)

  useEffect(() => {
    void paymentApi.getFees().then(setFees).catch(() => setFees(null))
  }, [])

  const handleReject = (reason?: string) => {
    if (rejectingId && reason) onReject(rejectingId, reason)
    setRejectingId(null)
  }

  // Soft visibility only, matching the codebase's stance that Amount is never hard-validated
  // against configured fees - just something for the admin to notice before clicking Verify.
  // NewMembership compares against the registration totals, Renewal against the renewal totals,
  // PortalAccessOnly against the bare portal fee alone (it's not a dues payment).
  const expectedTotalFor = (payment: Payment): number | null => {
    if (!fees) return null
    if (payment.kind === 'NewMembership') {
      return payment.includesPortalAccess ? fees.registrationTotalWithPortal : fees.registrationTotalWithoutPortal
    }
    if (payment.kind === 'PortalAccessOnly') {
      return fees.portalFee
    }
    return payment.includesPortalAccess ? fees.renewalTotalWithPortal : fees.renewalTotalWithoutPortal
  }

  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">Payments Awaiting Verification</h6>
      </div>

      <div className="flex flex-col">
        <div className="overflow-x-auto">
          <div className="min-w-full inline-block align-middle">
            <div className="overflow-hidden">
              <table className="min-w-full divide-y divide-default-200">
                <thead className="bg-default-150">
                  <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
                    <th className="px-3.5 py-3 text-start">Member</th>
                    <th className="px-3.5 py-3 text-start">Membership No.</th>
                    <th className="px-3.5 py-3 text-start">For</th>
                    <th className="px-3.5 py-3 text-start">Amount</th>
                    <th className="px-3.5 py-3 text-start">Reference</th>
                    <th className="px-3.5 py-3 text-start">Paid On</th>
                    <th className="px-3.5 py-3 text-start">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {payments.map((payment) => (
                    <tr key={payment.id} className="text-default-800 font-normal text-sm whitespace-nowrap">
                      <td className="py-3 px-3.5">
                        <Link to={`/members/${payment.memberId}`} className="font-semibold hover:text-primary">
                          {payment.memberName}
                        </Link>
                      </td>
                      <td className="py-3 px-3.5">
                        {payment.membershipNo ?? <span className="text-default-500">Not yet assigned</span>}
                      </td>
                      <td className="py-3 px-3.5">
                        {payment.kind === 'EventRegistration' && payment.eventTitle
                          ? `${KIND_LABELS[payment.kind]} — ${payment.eventTitle}`
                          : KIND_LABELS[payment.kind]}
                      </td>
                      <td className="py-3 px-3.5">
                        <div className="flex items-center gap-1.5">
                          {peso.format(payment.amount)}
                          {(() => {
                            const expected = expectedTotalFor(payment)
                            return expected !== null && expected !== payment.amount ? (
                              <LuTriangleAlert
                                className="size-4 shrink-0 text-warning"
                                title={`Doesn't match the expected total (${peso.format(expected)}) for ${
                                  payment.includesPortalAccess ? 'including' : 'not including'
                                } Portal Access.`}
                              />
                            ) : null
                          })()}
                        </div>
                      </td>
                      <td className="py-3 px-3.5">{payment.referenceNo || '-'}</td>
                      <td className="py-3 px-3.5">{new Date(payment.paidOn).toLocaleDateString()}</td>
                      <td className="py-3 px-3.5">
                        <div className="flex items-center gap-1.5">
                          {canManagePayments && (
                            <>
                              {/* Verifying without looking at the proof is the mistake this queue
                                  exists to prevent, so the button is disabled when there's nothing
                                  to look at. */}
                              <StandardButton
                                variant="success"
                                size="sm"
                                icon={LuCheck}
                                disabled={!payment.hasProof}
                                onClick={() => onVerify(payment.id)}
                              >
                                Verify
                              </StandardButton>
                              <StandardButton variant="danger" size="sm" icon={LuX} onClick={() => setRejectingId(payment.id)}>
                                Reject
                              </StandardButton>
                            </>
                          )}
                          {payment.hasProof && (
                            <StandardButton variant="view" size="sm" icon={LuEye} onClick={() => setPreviewingId(payment.id)}>
                              View proof
                            </StandardButton>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                  {payments.length === 0 && (
                    <tr>
                      <td colSpan={7} className="py-6 px-3.5 text-center text-default-500">
                        No payments awaiting verification.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>

      <div className="card-footer flex items-center justify-between">
        <span className="text-sm text-default-500">
          Page {page} of {totalPages} ({totalCount} total)
        </span>
        <div className="flex items-center gap-1.5">
          <button
            type="button"
            className="btn btn-sm border border-default-200 disabled:opacity-50"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
          >
            Previous
          </button>
          <button
            type="button"
            className="btn btn-sm border border-default-200 disabled:opacity-50"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
          >
            Next
          </button>
        </div>
      </div>

      <ConfirmationModal
        isOpen={rejectingId !== null}
        title="Reject this payment?"
        message="The member is told why and can submit another. Their membership status and renewal date are left unchanged."
        confirmLabel="Reject"
        confirmVariant="danger"
        reasonRequired
        onConfirm={handleReject}
        onCancel={() => setRejectingId(null)}
      />

      {previewingId && (
        <FilePreviewModal
          isOpen
          title="Proof of Payment"
          fetchFile={() => paymentApi.fetchProofUrl(previewingId)}
          onClose={() => setPreviewingId(null)}
          genericErrorMessage={PROOF_MISSING_MESSAGE}
        />
      )}
    </div>
  )
}
