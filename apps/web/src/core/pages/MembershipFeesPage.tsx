import { useEffect, useState } from 'react'
import { paymentApi, type FeePromotion } from '../api/endpoints/paymentApi'
import { describeError } from '../utils/apiError'
import { useAuth } from '../auth/useAuth'
import { Roles } from '../types/auth'
import { PageBreadcrumb, PageMeta, StandardButton } from '../../integrations/template'
import { ConfirmationModal } from '../../integrations/template/components/shared/ConfirmationModal'

const peso = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })

/** The four MembershipFeeKeys values (see src/PSMPE.Portal.Application/Common/Configuration/
 *  MembershipFeeKeys.cs) paired with a human label - the raw keys aren't display-friendly. */
const FEE_KEY_OPTIONS: { key: string; label: string }[] = [
  { key: 'MembershipFee', label: 'Membership Fee' },
  { key: 'MembershipShippingFee', label: 'Shipping Fee' },
  { key: 'AnnualDues', label: 'Annual Dues' },
  { key: 'PortalFee', label: 'Portal Fee' },
]
const FEE_KEY_LABELS: Record<string, string> = Object.fromEntries(FEE_KEY_OPTIONS.map((o) => [o.key, o.label]))

type PromotionStatus = 'Active' | 'Upcoming' | 'Expired'

/** ISO date-only strings ("2026-08-29") sort and compare lexicographically, so plain string
 *  comparison against today's own ISO date avoids any Date/timezone parsing entirely. */
function promotionStatus(promotion: FeePromotion): PromotionStatus {
  const today = new Date().toISOString().slice(0, 10)
  if (today < promotion.startDate) return 'Upcoming'
  if (today > promotion.endDate) return 'Expired'
  return 'Active'
}

const STATUS_BADGE_CLASS: Record<PromotionStatus, string> = {
  Active: 'bg-success/10 text-success',
  Upcoming: 'bg-info/10 text-info',
  Expired: 'bg-default-150 text-default-600',
}

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
  const [portalFee, setPortalFee] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const load = () =>
    paymentApi.getFees().then((fees) => {
      setMembershipFee(String(fees.membershipFee))
      setShippingFee(String(fees.shippingFee))
      setAnnualDues(String(fees.annualDues))
      setPortalFee(String(fees.portalFee))
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
    portalFee: Number(portalFee),
  }
  const allValid = Object.values(parsed).every((v) => Number.isFinite(v) && v >= 0)

  // Promotions panel - admin-only, same gate as fee editing below.
  const [promotions, setPromotions] = useState<FeePromotion[]>([])
  const [promotionsLoading, setPromotionsLoading] = useState(true)
  const [promotionsError, setPromotionsError] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<'All' | PromotionStatus>('All')

  const [newFeeKey, setNewFeeKey] = useState(FEE_KEY_OPTIONS[0].key)
  const [newAmount, setNewAmount] = useState('')
  const [singleDay, setSingleDay] = useState(false)
  const [newStartDate, setNewStartDate] = useState('')
  const [newEndDate, setNewEndDate] = useState('')
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)
  const [cancelling, setCancelling] = useState<FeePromotion | null>(null)

  const loadPromotions = () => paymentApi.getPromotions().then(setPromotions)

  useEffect(() => {
    if (!canEdit) {
      setPromotionsLoading(false)
      return
    }
    loadPromotions()
      .catch((err) => setPromotionsError(describeError(err, 'Could not load promotions.')))
      .finally(() => setPromotionsLoading(false))
    // canEdit is derived from the token and doesn't change within a session - this only ever runs once.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const filteredPromotions = promotions.filter(
    (promotion) => statusFilter === 'All' || promotionStatus(promotion) === statusFilter,
  )

  const newAmountParsed = Number(newAmount)
  const newPromotionValid =
    Number.isFinite(newAmountParsed) &&
    newAmountParsed >= 0 &&
    newStartDate !== '' &&
    (singleDay || (newEndDate !== '' && newEndDate >= newStartDate))

  const handleCreatePromotion = async () => {
    setCreateError(null)
    if (!newPromotionValid) {
      setCreateError('Pick a promo amount and valid date range.')
      return
    }

    setCreating(true)
    try {
      await paymentApi.createPromotion({
        feeKey: newFeeKey,
        promoAmount: newAmountParsed,
        startDate: newStartDate,
        endDate: singleDay ? newStartDate : newEndDate,
      })
      await loadPromotions()
      setNewAmount('')
      setNewStartDate('')
      setNewEndDate('')
      setSingleDay(false)
    } catch (err) {
      setCreateError(describeError(err, 'Could not create the promotion. Please try again.'))
    } finally {
      setCreating(false)
    }
  }

  const handleDeletePromotion = async (id: string) => {
    setPromotionsError(null)
    try {
      await paymentApi.deletePromotion(id)
      await loadPromotions()
    } catch (err) {
      setPromotionsError(describeError(err, 'Could not cancel the promotion. Please try again.'))
    }
  }

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

                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                  {field('membership-fee', 'Membership Fee', membershipFee, setMembershipFee, 'One-time, at registration.')}
                  {field('shipping-fee', 'Shipping Fee', shippingFee, setShippingFee, 'One-time, ID/card delivery.')}
                  {field('annual-dues', 'Annual Dues', annualDues, setAnnualDues, 'Charged each renewal year.')}
                  {field(
                    'portal-fee',
                    'Portal Fee',
                    portalFee,
                    setPortalFee,
                    'Optional add-on, every registration and renewal.',
                  )}
                </div>

                <div className="text-sm text-default-700 flex flex-col gap-1">
                  <p>
                    Registration total (without Portal Access):{' '}
                    <span className="font-semibold text-default-800">
                      {allValid ? peso.format(parsed.membershipFee + parsed.shippingFee) : '—'}
                    </span>
                  </p>
                  <p>
                    Registration total (with Portal Access):{' '}
                    <span className="font-semibold text-default-800">
                      {allValid ? peso.format(parsed.membershipFee + parsed.shippingFee + parsed.portalFee) : '—'}
                    </span>
                  </p>
                </div>
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

        {canEdit && (
          <div className="card max-w-4xl mt-6">
            <div className="card-header">
              <h6 className="card-title">Promotions</h6>
            </div>
            <div className="card-body flex flex-col gap-4">
              <p className="text-sm text-default-600">
                A promotion temporarily overrides one fee's amount for a date range - it starts and stops by
                itself, and never changes a payment already created while it was active. Overlapping promotions
                for the same fee are rejected.
              </p>

              {promotionsError && <p className="text-sm font-medium text-danger">{promotionsError}</p>}

              {promotionsLoading ? (
                <p className="text-sm text-default-500">Loading…</p>
              ) : (
                <>
                  <div className="flex flex-wrap items-center gap-3">
                    <label htmlFor="promotion-status-filter" className="text-sm font-medium text-default-900">
                      Status
                    </label>
                    <select
                      id="promotion-status-filter"
                      className="form-input max-w-40"
                      value={statusFilter}
                      onChange={(e) => setStatusFilter(e.target.value as 'All' | PromotionStatus)}
                    >
                      <option value="All">All</option>
                      <option value="Active">Active</option>
                      <option value="Upcoming">Upcoming</option>
                      <option value="Expired">Expired</option>
                    </select>
                  </div>

                  <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-default-200">
                      <thead className="bg-default-150">
                        <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
                          <th className="px-3.5 py-3 text-start">Fee</th>
                          <th className="px-3.5 py-3 text-start">Promo Amount</th>
                          <th className="px-3.5 py-3 text-start">Start Date</th>
                          <th className="px-3.5 py-3 text-start">End Date</th>
                          <th className="px-3.5 py-3 text-start">Status</th>
                          <th className="px-3.5 py-3 text-start">Actions</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-default-200">
                        {filteredPromotions.map((promotion) => {
                          const status = promotionStatus(promotion)
                          return (
                            <tr key={promotion.id} className="text-default-800 font-normal text-sm whitespace-nowrap">
                              <td className="py-3 px-3.5">{FEE_KEY_LABELS[promotion.feeKey] ?? promotion.feeKey}</td>
                              <td className="py-3 px-3.5">{peso.format(promotion.promoAmount)}</td>
                              <td className="py-3 px-3.5">{promotion.startDate}</td>
                              <td className="py-3 px-3.5">{promotion.endDate}</td>
                              <td className="py-3 px-3.5">
                                <span
                                  className={`py-0.5 px-2.5 inline-flex items-center text-xs font-medium rounded ${STATUS_BADGE_CLASS[status]}`}
                                >
                                  {status}
                                </span>
                              </td>
                              <td className="py-3 px-3.5">
                                <button
                                  type="button"
                                  className="text-sm text-danger hover:underline"
                                  onClick={() => setCancelling(promotion)}
                                >
                                  Cancel
                                </button>
                              </td>
                            </tr>
                          )
                        })}
                        {filteredPromotions.length === 0 && (
                          <tr>
                            <td colSpan={6} className="py-6 px-3.5 text-center text-default-500">
                              {promotions.length === 0 ? 'No promotions yet.' : 'No promotions match this filter.'}
                            </td>
                          </tr>
                        )}
                      </tbody>
                    </table>
                  </div>

                  <div className="border-t border-default-200 pt-4 flex flex-col gap-3">
                    <h6 className="font-medium text-default-900 text-sm">Add Promotion</h6>

                    {createError && <p className="text-sm font-medium text-danger">{createError}</p>}

                    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                      <div>
                        <label htmlFor="promotion-fee-key" className="block font-medium text-default-900 text-sm mb-2">
                          Fee
                        </label>
                        <select
                          id="promotion-fee-key"
                          className="form-input"
                          value={newFeeKey}
                          onChange={(e) => setNewFeeKey(e.target.value)}
                        >
                          {FEE_KEY_OPTIONS.map((option) => (
                            <option key={option.key} value={option.key}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                      </div>
                      <div>
                        <label htmlFor="promotion-amount" className="block font-medium text-default-900 text-sm mb-2">
                          Promo Amount
                        </label>
                        <input
                          id="promotion-amount"
                          className="form-input"
                          type="number"
                          min="0"
                          step="0.01"
                          value={newAmount}
                          onChange={(e) => setNewAmount(e.target.value)}
                        />
                      </div>
                      {singleDay ? (
                        <div>
                          <label htmlFor="promotion-single-date" className="block font-medium text-default-900 text-sm mb-2">
                            Date
                          </label>
                          <input
                            id="promotion-single-date"
                            className="form-input"
                            type="date"
                            value={newStartDate}
                            onChange={(e) => setNewStartDate(e.target.value)}
                          />
                        </div>
                      ) : (
                        <>
                          <div>
                            <label htmlFor="promotion-start-date" className="block font-medium text-default-900 text-sm mb-2">
                              Start Date
                            </label>
                            <input
                              id="promotion-start-date"
                              className="form-input"
                              type="date"
                              value={newStartDate}
                              onChange={(e) => setNewStartDate(e.target.value)}
                            />
                          </div>
                          <div>
                            <label htmlFor="promotion-end-date" className="block font-medium text-default-900 text-sm mb-2">
                              End Date
                            </label>
                            <input
                              id="promotion-end-date"
                              className="form-input"
                              type="date"
                              min={newStartDate || undefined}
                              value={newEndDate}
                              onChange={(e) => setNewEndDate(e.target.value)}
                            />
                          </div>
                        </>
                      )}
                    </div>

                    <label className="flex items-center gap-2 text-sm text-default-800">
                      <input
                        type="checkbox"
                        className="form-checkbox"
                        checked={singleDay}
                        onChange={(e) => setSingleDay(e.target.checked)}
                      />
                      Single day (Start Date = End Date)
                    </label>

                    <div>
                      <StandardButton
                        onClick={handleCreatePromotion}
                        loading={creating}
                        loadingLabel="Adding…"
                        disabled={!newPromotionValid}
                      >
                        Add Promotion
                      </StandardButton>
                    </div>
                  </div>
                </>
              )}
            </div>
          </div>
        )}
      </main>

      <ConfirmationModal
        isOpen={cancelling !== null}
        title="Cancel this promotion?"
        message={
          cancelling
            ? `This ends the promotional ${peso.format(cancelling.promoAmount)} price for ${
                FEE_KEY_LABELS[cancelling.feeKey] ?? cancelling.feeKey
              } immediately. It never affects a payment already created while it was active.`
            : undefined
        }
        confirmLabel="Cancel Promotion"
        confirmVariant="danger"
        onConfirm={() => {
          const id = cancelling?.id
          setCancelling(null)
          if (id) void handleDeletePromotion(id)
        }}
        onCancel={() => setCancelling(null)}
      />
    </>
  )
}
