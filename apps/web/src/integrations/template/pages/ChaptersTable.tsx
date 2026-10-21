import { LuPlus, LuSquarePen, LuTrash2 } from 'react-icons/lu'
import type { Chapter } from '../../../core/api/endpoints/chaptersApi'
import { StandardButton } from '../components/shared/StandardButton'

export type ChapterStatusFilter = '' | 'active' | 'inactive'

interface ChaptersTableProps {
  rows: Chapter[]
  loading: boolean
  searchInput: string
  onSearchInputChange: (value: string) => void
  statusFilter: ChapterStatusFilter
  onStatusFilterChange: (value: ChapterStatusFilter) => void
  onAdd: () => void
  onEdit: (row: Chapter) => void
  onDelete: (row: Chapter) => void
}

export const ChaptersTable = ({
  rows,
  loading,
  searchInput,
  onSearchInputChange,
  statusFilter,
  onStatusFilterChange,
  onAdd,
  onEdit,
  onDelete,
}: ChaptersTableProps) => (
  <div className="card">
    <div className="flex flex-wrap items-center gap-3 px-5 py-3 border-b border-default-200 bg-default-50">
      <input
        type="text"
        className="form-input max-w-xs"
        placeholder="Search by name…"
        aria-label="Search chapters"
        value={searchInput}
        onChange={(e) => onSearchInputChange(e.target.value)}
      />
      <select
        className="form-input max-w-xs"
        aria-label="Filter by status"
        value={statusFilter}
        onChange={(e) => onStatusFilterChange(e.target.value as ChapterStatusFilter)}
      >
        <option value="">All statuses</option>
        <option value="active">Active</option>
        <option value="inactive">Inactive</option>
      </select>
      <div className="ms-auto">
        <StandardButton size="sm" variant="on-primary" icon={LuPlus} onClick={onAdd}>
          Add chapter
        </StandardButton>
      </div>
    </div>

    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-default-200">
        <thead className="bg-default-150">
          <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
            <th className="px-3.5 py-3 text-start">Order</th>
            <th className="px-3.5 py-3 text-start">Chapter</th>
            <th className="px-3.5 py-3 text-start">Status</th>
            <th className="px-3.5 py-3 text-start">Members</th>
            <th className="px-3.5 py-3 text-start">Events</th>
            <th className="px-3.5 py-3 text-end">Actions</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-default-200">
          {rows.map((row) => (
            <tr key={row.id} className="text-default-800 font-normal text-sm whitespace-nowrap">
              <td className="py-3 px-3.5">{row.sortOrder}</td>
              <td className="py-3 px-3.5">{row.name}</td>
              <td className="py-3 px-3.5">
                <span
                  className={`text-xs font-medium px-2 py-0.5 rounded-full ${
                    row.isActive ? 'bg-success/20 text-success' : 'bg-default-200 text-default-600'
                  }`}
                >
                  {row.isActive ? 'Active' : 'Inactive'}
                </span>
              </td>
              <td className="py-3 px-3.5">{row.memberCount}</td>
              <td className="py-3 px-3.5">{row.eventCount}</td>
              <td className="py-3 px-3.5 text-end">
                <div className="inline-flex items-center gap-3">
                  <button
                    type="button"
                    className="text-primary hover:underline"
                    aria-label={`Edit ${row.name}`}
                    onClick={() => onEdit(row)}
                  >
                    <LuSquarePen className="size-4" />
                  </button>
                  <button
                    type="button"
                    className="text-danger hover:underline"
                    aria-label={`Delete ${row.name}`}
                    onClick={() => onDelete(row)}
                  >
                    <LuTrash2 className="size-4" />
                  </button>
                </div>
              </td>
            </tr>
          ))}
          {!loading && rows.length === 0 && (
            <tr>
              <td colSpan={6} className="py-6 px-3.5 text-center text-default-500">
                No chapters match this filter.
              </td>
            </tr>
          )}
          {loading && rows.length === 0 && (
            <tr>
              <td colSpan={6} className="py-6 px-3.5 text-center text-default-500">
                Loading…
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  </div>
)
