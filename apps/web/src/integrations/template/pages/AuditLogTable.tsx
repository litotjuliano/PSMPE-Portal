import { useState } from 'react'
import type { AuditLogEntry } from '../../../core/api/endpoints/systemLogsApi'
import { LogDetailsModal } from '../components/shared/LogDetailsModal'

const EVENT_TYPES = ['auth.rate_limit.rejected', 'auth.account.locked_out', 'auth.email_throttle.blocked', 'membership.approved']

interface AuditLogTableProps {
  entries: AuditLogEntry[]
  searchInput: string
  onSearchInputChange: (value: string) => void
  eventTypeFilter: string
  onEventTypeFilterChange: (value: string) => void
  from: string
  to: string
  onFromChange: (value: string) => void
  onToChange: (value: string) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

export const AuditLogTable = ({
  entries,
  searchInput,
  onSearchInputChange,
  eventTypeFilter,
  onEventTypeFilterChange,
  from,
  to,
  onFromChange,
  onToChange,
  page,
  pageSize,
  totalCount,
  onPageChange,
}: AuditLogTableProps) => {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const [detailsEntry, setDetailsEntry] = useState<AuditLogEntry | null>(null)

  return (
    <div className="card">
      <div className="flex flex-wrap items-center gap-3 px-5 py-3 border-b border-default-200 bg-default-50">
        <input
          type="text"
          className="form-input max-w-xs"
          placeholder="Search event type, target, metadata…"
          value={searchInput}
          onChange={(e) => onSearchInputChange(e.target.value)}
        />
        <select className="form-input max-w-xs" value={eventTypeFilter} onChange={(e) => onEventTypeFilterChange(e.target.value)}>
          <option value="">All event types</option>
          {EVENT_TYPES.map((type) => (
            <option key={type} value={type}>
              {type}
            </option>
          ))}
        </select>
        <input type="date" className="form-input" value={from} onChange={(e) => onFromChange(e.target.value)} />
        <span className="text-sm text-default-500">to</span>
        <input type="date" className="form-input" value={to} onChange={(e) => onToChange(e.target.value)} />
      </div>

      <div className="flex flex-col">
        <div className="overflow-x-auto">
          <div className="min-w-full inline-block align-middle">
            <div className="overflow-hidden">
              <table className="min-w-full divide-y divide-default-200">
                <thead className="bg-default-150">
                  <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
                    <th className="px-3.5 py-3 text-start">Timestamp</th>
                    <th className="px-3.5 py-3 text-start">Event Type</th>
                    <th className="px-3.5 py-3 text-start">Actor</th>
                    <th className="px-3.5 py-3 text-start">IP</th>
                    <th className="px-3.5 py-3 text-start">Target</th>
                    <th className="px-3.5 py-3 text-start">Details</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {entries.map((entry) => (
                    <tr key={entry.id} className="text-default-800 font-normal text-sm whitespace-nowrap">
                      <td className="py-3 px-3.5">{new Date(entry.createdAt).toLocaleString()}</td>
                      <td className="py-3 px-3.5">{entry.eventType}</td>
                      <td className="py-3 px-3.5">{entry.actorEmail ?? '—'}</td>
                      <td className="py-3 px-3.5">{entry.actorIp ?? '—'}</td>
                      <td className="py-3 px-3.5">{entry.targetType ? `${entry.targetType}: ${entry.targetId}` : '—'}</td>
                      <td className="py-3 px-3.5">
                        <button type="button" className="text-primary hover:underline" onClick={() => setDetailsEntry(entry)}>
                          View
                        </button>
                      </td>
                    </tr>
                  ))}
                  {entries.length === 0 && (
                    <tr>
                      <td colSpan={6} className="py-6 px-3.5 text-center text-default-500">
                        No audit events match this filter.
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

      <LogDetailsModal
        isOpen={detailsEntry !== null}
        title="Audit event details"
        content={detailsEntry?.metadata ?? null}
        onClose={() => setDetailsEntry(null)}
      />
    </div>
  )
}
