import { useCallback, useEffect, useRef, useState } from 'react'
import { LuTriangleAlert, LuUpload } from 'react-icons/lu'
import { paymentApi, type MembershipFees, type Payment } from '../../../../core/api/endpoints/paymentApi'
import type { Member } from '../../../../core/types/member'
import { describeError } from '../../../../core/utils/apiError'
import { StandardButton } from '../../components/shared/StandardButton'

interface RenewalPaymentCardProps {
  member: Member
  /** Called after a successful submission so the page can refresh derived state. */
  onSubmitted: () => void | Promise<void>
}

/** How far ahead of the due date the renewal form appears. A member who wants to pay early
 *  shouldn't have to wait for the deadline, but showing it 11 months out would be noise. */
const RENEWAL_WINDOW_DAYS = 60

const peso = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })

function daysUntil(dueDate: string): number {
  const due = new Date(`${dueDate}T00:00:00Z`)
  const today = new Date()
  return Math.ceil((due.getTime() - Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate())) / 86_400_000)
}

/**
 * The member's own view of where their dues stand, and the form to submit the next payment.
 *
 * Before this existed a member had no way to renew at all - renewal dates were typed in by an admin
 * and nothing in the portal ever asked for money. See openspecs/payments.md.
 */
export const RenewalPaymentCard = ({ member, onSubmitted }: RenewalPaymentCardProps) => {
  const [fees, setFees] = useState<MembershipFees | null>(null)
  const [payments, setPayments] = useState<Payment[]>([])
  const [amount, setAmount] = useState('')
  const [referenceNo, setReferenceNo] = useState('')
  const [paidOn, setPaidOn] = useState(() => new Date().toISOString().slice(0, 10))
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const proofInputRef = useRef<HTMLInputElement>(null)

  const load = useCallback(async () => {
    const [loadedFees, loadedPayments] = await Promise.all([paymentApi.getFees(), paymentApi.getMyPayments()])
    setFees(loadedFees)
    setPayments(loadedPayments)
    // Pre-filled with what's actually owed, so the common case is one click.
    setAmount((current) => current || String(loadedFees.annualDues))
  }, [])

  useEffect(() => {
    load().catch(() => setError('Could not load your payment details.'))
  }, [load])

  const pending = payments.find((p) => p.status === 'Submitted')
  const lastRejected = payments.find((p) => p.status === 'Rejected')

  const dueInDays = member.renewalDueDate ? daysUntil(member.renewalDueDate) : null
  const withinWindow = dueInDays !== null && dueInDays <= RENEWAL_WINDOW_DAYS

  const handleSubmit = async () => {
    setError(null)
    const parsed = Number(amount)
    if (!Number.isFinite(parsed) || parsed <= 0) {
      setError('Enter the amount you paid.')
      return
    }

    const file = proofInputRef.current?.files?.[0]
    if (!file) {
      setError('Attach your deposit slip or transfer screenshot.')
      return
    }

    setSubmitting(true)
    try {
      const payment = await paymentApi.submitMyPayment({
        amount: parsed,
        referenceNo: referenceNo.trim() || null,
        paidOn,
      })
      // Two calls because the proof is multipart - the same split the document uploads use. If this
      // one fails the payment exists without proof, which the member can see and re-attach.
      await paymentApi.uploadProof(payment.id, file)
      if (proofInputRef.current) proofInputRef.current.value = ''
      setReferenceNo('')
      await load()
      await onSubmitted()
    } catch (err) {
      setError(describeError(err, 'Could not submit your payment. Please try again.'))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">Membership Dues</h6>
      </div>
      <div className="card-body flex flex-col gap-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-sm">
          <div>
            <span className="block font-medium text-default-900 mb-1">Renewal Due</span>
            <span className="font-semibold text-default-800">
              {member.renewalDueDate ? new Date(member.renewalDueDate).toLocaleDateString() : 'Not set yet'}
            </span>
          </div>
          <div>
            <span className="block font-medium text-default-900 mb-1">Annual Dues</span>
            <span className="font-semibold text-default-800">{fees ? peso.format(fees.annualDues) : '—'}</span>
          </div>
        </div>

        {member.isExpired && (
          <p className="text-sm text-danger bg-danger/10 rounded-lg px-3 py-2 flex items-start gap-2">
            <LuTriangleAlert className="size-4 shrink-0 mt-0.5" />
            Your membership lapsed on {new Date(member.renewalDueDate!).toLocaleDateString()} and the grace period has
            ended. Submit your dues to restore it.
          </p>
        )}
        {!member.isExpired && member.isInGracePeriod && (
          <p className="text-sm text-warning bg-warning/10 rounded-lg px-3 py-2 flex items-start gap-2">
            <LuTriangleAlert className="size-4 shrink-0 mt-0.5" />
            Your dues were due on {new Date(member.renewalDueDate!).toLocaleDateString()}. You're in the grace period —
            renew now to avoid losing access.
          </p>
        )}
        {!member.isExpired && !member.isInGracePeriod && dueInDays !== null && withinWindow && (
          <p className="text-sm text-default-600">Your dues are due in {dueInDays} days.</p>
        )}

        {error && <p className="text-sm font-medium text-danger">{error}</p>}

        {pending ? (
          <p className="text-sm text-default-600 bg-default-100 rounded-lg px-3 py-2">
            You submitted {peso.format(pending.amount)} on {new Date(pending.paidOn).toLocaleDateString()}. It's waiting
            for verification — you'll be able to submit another once it's been reviewed.
          </p>
        ) : (
          <>
            {lastRejected?.rejectedReason && (
              <p className="text-sm text-danger bg-danger/10 rounded-lg px-3 py-2">
                Your last payment was rejected: {lastRejected.rejectedReason}
              </p>
            )}
            {(withinWindow || member.isInGracePeriod || member.isExpired || !member.renewalDueDate) && (
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                <div>
                  <label htmlFor="dues-amount" className="block font-medium text-default-900 text-sm mb-2">
                    Amount Paid
                  </label>
                  <input
                    id="dues-amount"
                    className="form-input"
                    type="number"
                    min="0"
                    step="0.01"
                    value={amount}
                    onChange={(e) => setAmount(e.target.value)}
                  />
                </div>
                <div>
                  <label htmlFor="dues-reference" className="block font-medium text-default-900 text-sm mb-2">
                    Reference No. (optional)
                  </label>
                  <input
                    id="dues-reference"
                    className="form-input"
                    value={referenceNo}
                    onChange={(e) => setReferenceNo(e.target.value)}
                  />
                </div>
                <div>
                  <label htmlFor="dues-paid-on" className="block font-medium text-default-900 text-sm mb-2">
                    Date Paid
                  </label>
                  <input
                    id="dues-paid-on"
                    className="form-input"
                    type="date"
                    max={new Date().toISOString().slice(0, 10)}
                    value={paidOn}
                    onChange={(e) => setPaidOn(e.target.value)}
                  />
                </div>
                <div className="sm:col-span-3 flex flex-wrap items-center gap-3">
                  <input ref={proofInputRef} type="file" accept="image/jpeg,image/png,application/pdf" className="text-sm" />
                  <StandardButton icon={LuUpload} onClick={handleSubmit} loading={submitting} loadingLabel="Submitting…">
                    Submit Payment
                  </StandardButton>
                  <span className="text-xs text-default-500">JPG, PNG or PDF, up to 1 MB.</span>
                </div>
              </div>
            )}
          </>
        )}

        {payments.length > 0 && (
          <div className="border-t border-default-200 pt-4">
            <h6 className="font-semibold text-default-800 mb-3 text-sm">Payment History</h6>
            <div className="overflow-x-auto">
              <table className="min-w-full text-sm">
                <thead>
                  <tr className="text-default-700 text-start">
                    <th className="py-2 pe-4 text-start font-medium">Date</th>
                    <th className="py-2 pe-4 text-start font-medium">For</th>
                    <th className="py-2 pe-4 text-start font-medium">Amount</th>
                    <th className="py-2 pe-4 text-start font-medium">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {payments.map((payment) => (
                    <tr key={payment.id}>
                      <td className="py-2 pe-4">{new Date(payment.paidOn).toLocaleDateString()}</td>
                      <td className="py-2 pe-4">{payment.kind === 'Renewal' ? 'Renewal' : 'New membership'}</td>
                      <td className="py-2 pe-4">{peso.format(payment.amount)}</td>
                      <td className="py-2 pe-4">
                        <span
                          className={
                            payment.status === 'Verified'
                              ? 'text-success font-medium'
                              : payment.status === 'Rejected'
                                ? 'text-danger font-medium'
                                : 'text-default-600'
                          }
                        >
                          {payment.status}
                        </span>
                        {payment.status === 'Rejected' && payment.rejectedReason && (
                          <span className="block text-xs text-default-500">{payment.rejectedReason}</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
