import { useMemo, useState } from 'react'
import type { EventRosterEntry, EventSession } from '../../../core/api/endpoints/eventApi'
import { StandardButton } from '../components/shared/StandardButton'

interface EventRosterTableProps {
  sessions: EventSession[]
  registrants: EventRosterEntry[]
  pendingAttendance: Record<string, Set<string>>
  onToggleSession: (registrationId: string, sessionId: string) => void
  onSaveAttendance: () => void
  savingAttendance: boolean
  hasUnsavedChanges: boolean
  onRecordCashPayment: (registrationId: string, amount: number) => void
}

/** Raw `EventRosterEntry.paymentStatus` values (mirrors the backend's PaymentStatus enum), plus the
 *  "no payment at all" case surfaced as null - used for the status filter below. "Pending" is the
 *  admin-facing label for a Submitted payment awaiting verification. */
const PAYMENT_STATUS_FILTERS: { value: string; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Verified', label: 'Verified' },
  { value: 'Submitted', label: 'Pending' },
  { value: 'Rejected', label: 'Rejected' },
]

function paymentBadge(entry: EventRosterEntry) {
  if (!entry.paymentStatus) return <span className="text-xs text-default-400">No payment</span>
  const label = entry.paymentIsCash ? `${entry.paymentStatus} (cash)` : entry.paymentStatus
  const cls =
    entry.paymentStatus === 'Verified'
      ? 'bg-success/10 text-success'
      : entry.paymentStatus === 'Rejected'
        ? 'bg-danger/10 text-danger'
        : 'bg-warning/10 text-warning'
  return <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs ${cls}`}>{label}</span>
}

/** Per-session checkboxes reflect `pendingAttendance` (the in-progress edit), not
 *  `entry.attendedSessionIds` directly - EventRosterPage seeds pendingAttendance from the fetched
 *  roster and only writes it back to the server when the admin clicks Save, so partially-checked
 *  work isn't lost mid-reconciliation across a slow page. */
export function EventRosterTable({
  sessions,
  registrants,
  pendingAttendance,
  onToggleSession,
  onSaveAttendance,
  savingAttendance,
  hasUnsavedChanges,
  onRecordCashPayment,
}: EventRosterTableProps) {
  const [cashAmount, setCashAmount] = useState<Record<string, string>>({})
  const [searchInput, setSearchInput] = useState('')
  const [statusFilter, setStatusFilter] = useState('')

  // Client-side only - the roster is already fully loaded in memory for one event, so there's no
  // need for a server round trip just to search/filter within it.
  const filteredRegistrants = useMemo(() => {
    const search = searchInput.trim().toLowerCase()
    return registrants.filter((entry) => {
      const matchesSearch =
        search === '' ||
        entry.memberName.toLowerCase().includes(search) ||
        (entry.membershipNo?.toLowerCase().includes(search) ?? false)
      const matchesStatus = statusFilter === '' || entry.paymentStatus === statusFilter
      return matchesSearch && matchesStatus
    })
  }, [registrants, searchInput, statusFilter])

  return (
    <div className="card">
      <div className="card-header flex items-center justify-between">
        <h6 className="card-title">Roster</h6>
        <div className="flex items-center gap-3">
          {hasUnsavedChanges && (
            <span className="text-xs font-normal normal-case text-white/90 bg-white/10 px-2.5 py-1 rounded-full">
              Unsaved attendance changes
            </span>
          )}
          <StandardButton onClick={onSaveAttendance} loading={savingAttendance} size="sm" variant="on-primary">
            Save Attendance
          </StandardButton>
        </div>
      </div>

      <div className="card-header flex flex-wrap items-center gap-3 border-t border-default-200">
        <input
          type="text"
          className="form-input max-w-xs"
          placeholder="Search by name or membership no…"
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
        />
        <select className="form-input max-w-40" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
          {PAYMENT_STATUS_FILTERS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </div>

      <div className="flex flex-col">
        <div className="overflow-x-auto">
          <div className="min-w-full inline-block align-middle">
            <div className="overflow-hidden">
              <table className="min-w-full divide-y divide-default-200">
                <thead className="bg-default-150">
                  <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
                    <th className="px-3.5 py-3 text-start">Member</th>
                    <th className="px-3.5 py-3 text-start">Mode</th>
                    <th className="px-3.5 py-3 text-start">Payment</th>
                    {sessions.map((s) => (
                      <th key={s.id} className="px-3.5 py-3 text-center">
                        {s.title}
                      </th>
                    ))}
                    <th className="px-3.5 py-3 text-start">Status</th>
                    <th className="px-3.5 py-3 text-start">Evaluation</th>
                    <th className="px-3.5 py-3 text-start">Credit</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {filteredRegistrants.map((entry) => (
                    <tr key={entry.registrationId} className="text-default-800 font-normal text-sm whitespace-nowrap">
                      <td className="py-3 px-3.5">
                        <div className="font-semibold">{entry.memberName}</div>
                        <div className="text-xs text-default-400">{entry.membershipNo ?? '-'}</div>
                      </td>
                      <td className="py-3 px-3.5">{entry.mode}</td>
                      <td className="py-3 px-3.5">
                        <div className="flex flex-col gap-1.5">
                          {paymentBadge(entry)}
                          {!entry.paymentId || entry.paymentStatus === 'Rejected' ? (
                            <div className="flex gap-1.5">
                              <input
                                type="number"
                                className="form-input form-input-sm w-20"
                                placeholder="Amount"
                                value={cashAmount[entry.registrationId] ?? ''}
                                onChange={(e) =>
                                  setCashAmount((prev) => ({ ...prev, [entry.registrationId]: e.target.value }))
                                }
                              />
                              <StandardButton
                                variant="secondary"
                                size="sm"
                                onClick={() =>
                                  onRecordCashPayment(entry.registrationId, Number(cashAmount[entry.registrationId] ?? '0'))
                                }
                              >
                                Record Cash
                              </StandardButton>
                            </div>
                          ) : null}
                        </div>
                      </td>
                      {sessions.map((s) => (
                        <td key={s.id} className="py-3 px-3.5 text-center">
                          <input
                            type="checkbox"
                            className="form-checkbox"
                            disabled={entry.paymentStatus !== 'Verified'}
                            checked={pendingAttendance[entry.registrationId]?.has(s.id) ?? false}
                            onChange={() => onToggleSession(entry.registrationId, s.id)}
                          />
                        </td>
                      ))}
                      <td className="py-3 px-3.5">{entry.status}</td>
                      <td className="py-3 px-3.5">{entry.evaluationRating ?? '-'}</td>
                      <td className="py-3 px-3.5">{entry.creditUnits ?? '-'}</td>
                    </tr>
                  ))}
                  {filteredRegistrants.length === 0 && (
                    <tr>
                      <td colSpan={6 + sessions.length} className="py-6 px-3.5 text-center text-default-500">
                        {registrants.length === 0 ? 'No registrants yet.' : 'No registrants match your search.'}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
