import { useEffect, useState } from 'react'
import { paymentApi, type PaymentReportSummary } from '../../../core/api/endpoints/paymentApi'
import { describeError } from '../../../core/utils/apiError'

const peso = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })

/** Local-date (not UTC) YYYY-MM-DD, matching the plain <input type="date"> value format and this
 *  codebase's other date-only fields (paidOn, promotion startDate/endDate, etc). */
function toIsoDate(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

const QUICK_PICKS = ['This month', 'Last month', 'Last 3 months', 'Last 6 months', 'This year'] as const
type QuickPick = (typeof QUICK_PICKS)[number]

/** Each quick-pick just computes a start/end pair and writes it into the same two date inputs a
 *  custom range would - there's one source of truth (the two dates), not a separate "mode". */
function rangeFor(pick: QuickPick, today: Date): { startDate: string; endDate: string } {
  const year = today.getFullYear()
  const month = today.getMonth()
  switch (pick) {
    case 'This month':
      return { startDate: toIsoDate(new Date(year, month, 1)), endDate: toIsoDate(today) }
    case 'Last month':
      return { startDate: toIsoDate(new Date(year, month - 1, 1)), endDate: toIsoDate(new Date(year, month, 0)) }
    case 'Last 3 months':
      return { startDate: toIsoDate(new Date(year, month - 2, 1)), endDate: toIsoDate(today) }
    case 'Last 6 months':
      return { startDate: toIsoDate(new Date(year, month - 5, 1)), endDate: toIsoDate(today) }
    case 'This year':
      return { startDate: toIsoDate(new Date(year, 0, 1)), endDate: toIsoDate(today) }
  }
}

/**
 * Admin Payments tab summary panel - a separate, self-contained card next to PaymentsQueueTable
 * (same self-containment pattern: its own date-range state and its own fetch), not folded into the
 * queue table itself. See openspecs/payments.md and tasks.md section 7.
 */
export const PaymentsSummaryPanel = () => {
  const [startDate, setStartDate] = useState(() => rangeFor('This month', new Date()).startDate)
  const [endDate, setEndDate] = useState(() => rangeFor('This month', new Date()).endDate)
  // Drives the <select>'s value so it's controlled rather than defaultValue-only: without this,
  // editing the date inputs directly then re-selecting the *same already-displayed* option is a
  // no-op (the DOM value never changed, so the browser never fires onChange), leaving the stale
  // custom range in place. Tracking the pick as state also gives the dropdown an honest "custom"
  // label once the two dates have diverged from any preset.
  const [pick, setPick] = useState<QuickPick | 'custom'>('This month')
  const [summary, setSummary] = useState<PaymentReportSummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    paymentApi
      .getReportSummary(startDate, endDate)
      .then((result) => {
        if (cancelled) return
        setSummary(result)
      })
      .catch((err) => {
        // Leave the previous figures (or the initial placeholder) visible rather than blanking
        // the panel on a bad range - the message alone is enough to explain nothing changed.
        if (!cancelled) setError(describeError(err, 'Could not load the payment summary. Please try again.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [startDate, endDate])

  const applyQuickPick = (nextPick: QuickPick) => {
    const range = rangeFor(nextPick, new Date())
    setPick(nextPick)
    setStartDate(range.startDate)
    setEndDate(range.endDate)
  }

  return (
    <div className="card mb-6">
      <div className="card-header flex items-center justify-between">
        <h6 className="card-title">Payment Summary</h6>
        {/* Only shown while a summary is already on screen - the initial load has no figures to
            keep visible, so it's covered by the placeholder dashes below instead. */}
        {loading && summary && <span className="text-xs text-default-500">Updating…</span>}
      </div>
      <div className="card-body flex flex-col gap-4">
        <div className="flex flex-wrap items-end gap-3">
          <div>
            <label htmlFor="summary-quick-pick" className="block font-medium text-default-900 text-sm mb-2">
              Quick Range
            </label>
            <select
              id="summary-quick-pick"
              className="form-input max-w-44"
              value={pick}
              onChange={(e) => applyQuickPick(e.target.value as QuickPick)}
            >
              {/* Only shown while diverged - never a choice the user picks to get here, just an
                  honest label for "these two dates don't match any preset right now". */}
              {pick === 'custom' && <option value="custom">Custom range</option>}
              {QUICK_PICKS.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="summary-start-date" className="block font-medium text-default-900 text-sm mb-2">
              Start Date
            </label>
            <input
              id="summary-start-date"
              className="form-input"
              type="date"
              value={startDate}
              max={endDate}
              onChange={(e) => {
                setPick('custom')
                setStartDate(e.target.value)
              }}
            />
          </div>
          <div>
            <label htmlFor="summary-end-date" className="block font-medium text-default-900 text-sm mb-2">
              End Date
            </label>
            <input
              id="summary-end-date"
              className="form-input"
              type="date"
              value={endDate}
              min={startDate}
              onChange={(e) => {
                setPick('custom')
                setEndDate(e.target.value)
              }}
            />
          </div>
        </div>

        {error && (
          <p className="text-sm font-medium text-danger">
            {error}
            {summary && ' The figures below are from the last valid range.'}
          </p>
        )}

        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div className="rounded-lg bg-default-100 px-4 py-3">
            <span className="block text-sm font-medium text-default-700 mb-1">Membership Only</span>
            <span className="block text-lg font-semibold text-default-900">
              {summary ? peso.format(summary.membershipOnlyTotal) : loading ? '…' : '—'}
            </span>
            <span className="block text-xs text-default-500">
              {summary ? `${summary.membershipOnlyCount} payment(s)` : loading ? 'Loading…' : ''}
            </span>
          </div>
          <div className="rounded-lg bg-default-100 px-4 py-3">
            <span className="block text-sm font-medium text-default-700 mb-1">Combined (Membership + Portal)</span>
            <span className="block text-lg font-semibold text-default-900">
              {summary ? peso.format(summary.combinedTotal) : loading ? '…' : '—'}
            </span>
            <span className="block text-xs text-default-500">
              {summary ? `${summary.combinedCount} payment(s)` : loading ? 'Loading…' : ''}
            </span>
          </div>
          <div className="rounded-lg bg-primary/10 px-4 py-3">
            <span className="block text-sm font-medium text-default-700 mb-1">Portal Revenue Collected</span>
            <span className="block text-lg font-semibold text-default-900">
              {summary ? peso.format(summary.portalRevenueTotal) : loading ? '…' : '—'}
            </span>
            {/* No count here - this is a sub-total of the Combined bucket above, not a third
                category of payments. */}
          </div>
        </div>
      </div>
    </div>
  )
}
