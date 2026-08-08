import { useState } from 'react'
import { Link } from 'react-router-dom'
import { LuCheck, LuEye, LuX } from 'react-icons/lu'
import { paymentApi, type Payment } from '../../../core/api/endpoints/paymentApi'
import { ConfirmationModal } from '../components/shared/ConfirmationModal'
import { FilePreviewModal } from '../components/shared/FilePreviewModal'
import { StandardButton } from '../components/shared/StandardButton'

interface PaymentsQueueTableProps {
  payments: Payment[]
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
}

const peso = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })

/**
 * The admin payment queue. Unlike the other three Members tabs this lists *payments*, not members,
 * so it has its own fetch and its own table - see openspecs/payments.md for why it still lives as a
 * tab rather than a fourth nav entry.
 */
export const PaymentsQueueTable = ({
  payments,
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

  const handleReject = (reason?: string) => {
    if (rejectingId && reason) onReject(rejectingId, reason)
    setRejectingId(null)
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
                      <td className="py-3 px-3.5">{KIND_LABELS[payment.kind]}</td>
                      <td className="py-3 px-3.5">{peso.format(payment.amount)}</td>
                      <td className="py-3 px-3.5">{payment.referenceNo || '-'}</td>
                      <td className="py-3 px-3.5">{new Date(payment.paidOn).toLocaleDateString()}</td>
                      <td className="py-3 px-3.5">
                        <div className="flex items-center gap-1.5">
                          {/* Verifying without looking at the proof is the mistake this queue exists
                              to prevent, so the button is disabled when there's nothing to look at. */}
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
        />
      )}
    </div>
  )
}
