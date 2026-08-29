import { Link } from 'react-router-dom'
import { LuCalendar, LuMapPin, LuPlus, LuSearch } from 'react-icons/lu'
import type { Event } from '../../../core/api/endpoints/eventApi'
import { Chapters, type ChapterValue } from '../../../core/types/member'
import { StandardButton } from '../components/shared/StandardButton'

interface EventsTableProps {
  events: Event[]
  canManageEvents: boolean
  searchInput: string
  onSearchInputChange: (value: string) => void
  chapterFilter: ChapterValue | null
  onChapterFilterChange: (chapter: ChapterValue | null) => void
  upcomingOnly: boolean
  onUpcomingOnlyChange: (value: boolean) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
  onNewEvent: () => void
  onSelectEvent: (event: Event) => void
}

function formatCpdUnits(onsite: number | null, online: number | null) {
  if (onsite === null && online === null) return 'CPD units: TBD'
  return `CPD units: Onsite ${onsite ?? 'TBD'} / Online ${online ?? 'TBD'}`
}

export function EventsTable({
  events,
  canManageEvents,
  searchInput,
  onSearchInputChange,
  chapterFilter,
  onChapterFilterChange,
  upcomingOnly,
  onUpcomingOnlyChange,
  page,
  pageSize,
  totalCount,
  onPageChange,
  onNewEvent,
  onSelectEvent,
}: EventsTableProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  return (
    <div className="card">
      <div className="card-header flex justify-between items-center">
        <h6 className="card-title">Events</h6>
        {canManageEvents && (
          <StandardButton onClick={onNewEvent} size="sm" variant="on-primary" icon={LuPlus}>
            New Event
          </StandardButton>
        )}
      </div>

      <div className="card-header flex flex-wrap items-center gap-3 border-t border-default-200">
        <div className="relative">
          <LuSearch className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-default-400" />
          <input
            type="text"
            className="form-input max-w-xs pl-9"
            placeholder="Search events…"
            value={searchInput}
            onChange={(e) => onSearchInputChange(e.target.value)}
          />
        </div>
        <select
          className="form-input max-w-48"
          value={chapterFilter ?? ''}
          onChange={(e) => onChapterFilterChange((e.target.value || null) as ChapterValue | null)}
        >
          <option value="">All chapters</option>
          {Object.values(Chapters).map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
        <label className="flex items-center gap-2 text-sm text-white/90">
          <input
            type="checkbox"
            className="form-checkbox"
            checked={upcomingOnly}
            onChange={(e) => onUpcomingOnlyChange(e.target.checked)}
          />
          Upcoming only
        </label>
      </div>

      <div className="card-body p-0">
        {events.length === 0 ? (
          <p className="text-sm text-default-500 p-4">No events found.</p>
        ) : (
          <ul className="divide-y divide-default-200">
            {events.map((event) => (
              <li key={event.id} className="p-4 hover:bg-default-50 cursor-pointer" onClick={() => onSelectEvent(event)}>
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-medium text-default-800">{event.title}</p>
                    <p className="flex items-center gap-1 text-xs text-default-500 mt-1">
                      <LuCalendar className="size-3.5" />
                      {new Date(event.startsAt).toLocaleDateString()} - {new Date(event.endsAt).toLocaleDateString()}
                    </p>
                    {event.venue && (
                      <p className="flex items-center gap-1 text-xs text-default-500">
                        <LuMapPin className="size-3.5" /> {event.venue}
                      </p>
                    )}
                    <p className="text-xs text-default-500 mt-1">{formatCpdUnits(event.cpdUnitsOnsite, event.cpdUnitsOnline)}</p>
                  </div>
                  <div className="text-right shrink-0">
                    <p className="text-sm font-semibold">
                      {event.feeOnsite > 0 || event.feeOnline > 0
                        ? `Onsite PHP ${event.feeOnsite.toFixed(2)} / Online PHP ${event.feeOnline.toFixed(2)}`
                        : 'Free'}
                    </p>
                    <p className="text-xs text-default-500">
                      {event.registeredCount}
                      {event.capacity ? ` / ${event.capacity}` : ''} registered
                    </p>
                    {canManageEvents && (
                      <Link
                        to={`/events/${event.id}/roster`}
                        onClick={(e) => e.stopPropagation()}
                        className="text-xs text-primary hover:underline"
                      >
                        View roster
                      </Link>
                    )}
                  </div>
                </div>
              </li>
            ))}
          </ul>
        )}
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
    </div>
  )
}
