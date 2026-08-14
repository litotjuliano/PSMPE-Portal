import { useState } from 'react'
import { ErrorSource, type ErrorLogEntry } from '../../../core/api/endpoints/systemLogsApi'
import { LogDetailsModal } from '../components/shared/LogDetailsModal'

interface ErrorLogTableProps {
  entries: ErrorLogEntry[]
  searchInput: string
  onSearchInputChange: (value: string) => void
  sourceFilter: string
  onSourceFilterChange: (value: string) => void
  from: string
  to: string
  onFromChange: (value: string) => void
  onToChange: (value: string) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

export const ErrorLogTable = ({
  entries,
  searchInput,
  onSearchInputChange,
  sourceFilter,
  onSourceFilterChange,
  from,
  to,
  onFromChange,
  onToChange,
  page,
  pageSize,
  totalCount,
  onPageChange,
}: ErrorLogTableProps) => {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const [detailsEntry, setDetailsEntry] = useState<ErrorLogEntry | null>(null)

  return (
    <div className="card">
      <div className="flex flex-wrap items-center gap-3 px-5 py-3 border-b border-default-200 bg-default-50">
        <input
          type="text"
          className="form-input max-w-xs"
          placeholder="Search message, exception type, path…"
          value={searchInput}
          onChange={(e) => onSearchInputChange(e.target.value)}
        />
        <select className="form-input max-w-xs" value={sourceFilter} onChange={(e) => onSourceFilterChange(e.target.value)}>
          <option value="">All sources</option>
          <option value={ErrorSource.Backend}>Backend</option>
          <option value={ErrorSource.Frontend}>Frontend</option>
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
                    <th className="px-3.5 py-3 text-start">Source</th>
                    <th className="px-3.5 py-3 text-start">Exception Type</th>
                    <th className="px-3.5 py-3 text-start">Message</th>
                    <th className="px-3.5 py-3 text-start">User</th>
                    <th className="px-3.5 py-3 text-start">Path / URL</th>
                    <th className="px-3.5 py-3 text-start">Details</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {entries.map((entry) => (
                    <tr key={entry.id} className="text-default-800 font-normal text-sm whitespace-nowrap">
                      <td className="py-3 px-3.5">{new Date(entry.createdAt).toLocaleString()}</td>
                      <td className="py-3 px-3.5">
                        <span
                          className={`py-0.5 px-2.5 text-xs font-medium rounded ${entry.source === ErrorSource.Backend ? 'bg-primary/10 text-primary' : 'bg-warning/10 text-warning'}`}
                        >
                          {entry.source === ErrorSource.Backend ? 'Backend' : 'Frontend'}
                        </span>
                      </td>
                      <td className="py-3 px-3.5">{entry.exceptionType ?? '—'}</td>
                      <td className="py-3 px-3.5 max-w-xs truncate">{entry.message}</td>
                      <td className="py-3 px-3.5">{entry.userEmail ?? '—'}</td>
                      <td className="py-3 px-3.5 max-w-xs truncate">{entry.requestPath ?? entry.url ?? '—'}</td>
                      <td className="py-3 px-3.5">
                        <button type="button" className="text-primary hover:underline" onClick={() => setDetailsEntry(entry)}>
                          View
                        </button>
                      </td>
                    </tr>
                  ))}
                  {entries.length === 0 && (
                    <tr>
                      <td colSpan={7} className="py-6 px-3.5 text-center text-default-500">
                        No errors match this filter.
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
        title="Error details"
        content={detailsEntry?.stackTrace ?? null}
        onClose={() => setDetailsEntry(null)}
      />
    </div>
  )
}
