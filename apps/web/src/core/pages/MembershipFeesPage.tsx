import { useEffect, useState } from 'react'
import { paymentApi } from '../api/endpoints/paymentApi'
import { describeError } from '../utils/apiError'
import { useAuth } from '../auth/useAuth'
import { Roles } from '../types/auth'
import { PageBreadcrumb, PageMeta, StandardButton } from '../../integrations/template'

const peso = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })

/**
 * The first admin-editable system configuration screen. Deliberately scoped to the three membership
 * fees rather than a general SystemConfig editor - fees are the values that actually change, and a
 * config CMS is a much larger thing to design than this change needed.
 */
export function MembershipFeesPage() {
  const { user } = useAuth()
  // False for an Approval user: fees are Members.Manage-gated server side, which they don't hold.
  const canEdit = user?.roles.includes(Roles.Admin) || user?.roles.includes(Roles.SuperAdmin) || false

  const [membershipFee, setMembershipFee] = useState('')
  const [shippingFee, setShippingFee] = useState('')
  const [annualDues, setAnnualDues] = useState('')
  // Not yet editable on this page - the Portal Fee field and Promotions panel are a separate,
  // larger addition. Round-tripped as-is so saving the three fields above never resets it to 0.
  const [portalFee, setPortalFee] = useState(0)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const load = () =>
    paymentApi.getFees().then((fees) => {
      setMembershipFee(String(fees.membershipFee))
      setShippingFee(String(fees.shippingFee))
      setAnnualDues(String(fees.annualDues))
      setPortalFee(fees.portalFee)
    })

  useEffect(() => {
    load()
      .catch((err) => setError(describeError(err, 'Could not load the current fees.')))
      .finally(() => setLoading(false))
  }, [])

  const parsed = {
    membershipFee: Number(membershipFee),
    shippingFee: Number(shippingFee),
    annualDues: Number(annualDues),
    // Not yet editable on this page - round-tripped as-is (see the state comment above).
    portalFee,
  }
  const allValid = Object.values(parsed).every((v) => Number.isFinite(v) && v >= 0)

  const handleSave = async () => {
    setError(null)
    setSaved(false)
    if (!allValid) {
      setError('Every fee must be zero or more.')
      return
    }

    setSaving(true)
    try {
      await paymentApi.updateFees(parsed)
      await load()
      setSaved(true)
    } catch (err) {
      setError(describeError(err, 'Could not save the fees. Please try again.'))
    } finally {
      setSaving(false)
    }
  }

  const field = (id: string, label: string, value: string, onChange: (next: string) => void, hint: string) => (
    <div>
      <label htmlFor={id} className="block font-medium text-default-900 text-sm mb-2">
        {label}
      </label>
      <input
        id={id}
        className="form-input"
        type="number"
        min="0"
        step="0.01"
        value={value}
        readOnly={!canEdit}
        onChange={(e) => onChange(e.target.value)}
      />
      <p className="text-xs text-default-500 mt-1">{hint}</p>
    </div>
  )

  return (
    <>
      <PageMeta title="Membership Fees" />
      <main>
        <PageBreadcrumb title="Membership Fees" />
        <div className="card max-w-2xl">
          <div className="card-header">
            <h6 className="card-title">Membership Fees</h6>
          </div>
          <div className="card-body flex flex-col gap-4">
            {loading ? (
              <p className="text-sm text-default-500">Loading…</p>
            ) : (
              <>
                <p className="text-sm text-default-600">
                  These figures drive the registration wizard's Payment Details step, the generated
                  receipt, and the amount pre-filled on a member's renewal form. Changing them takes
                  effect immediately.
                </p>

                {error && <p className="text-sm font-medium text-danger">{error}</p>}
                {saved && <p className="text-sm font-medium text-success">Fees saved.</p>}

                <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                  {field('membership-fee', 'Membership Fee', membershipFee, setMembershipFee, 'One-time, at registration.')}
                  {field('shipping-fee', 'Shipping Fee', shippingFee, setShippingFee, 'One-time, ID/card delivery.')}
                  {field('annual-dues', 'Annual Dues', annualDues, setAnnualDues, 'Charged each renewal year.')}
                </div>

                <p className="text-sm text-default-700">
                  Registration total:{' '}
                  <span className="font-semibold text-default-800">
                    {allValid ? peso.format(parsed.membershipFee + parsed.shippingFee) : '—'}
                  </span>
                </p>
              </>
            )}
          </div>
          {!loading && canEdit && (
            <div className="card-footer flex items-center justify-end">
              <StandardButton onClick={handleSave} loading={saving} loadingLabel="Saving…" disabled={!allValid}>
                Save Fees
              </StandardButton>
            </div>
          )}
        </div>
      </main>
    </>
  )
}
