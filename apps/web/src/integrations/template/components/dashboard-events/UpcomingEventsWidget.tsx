import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { LuCalendarClock, LuMapPin } from 'react-icons/lu'
import { StatTile } from '../shared/StatTile'
import type { Event } from '../../../../core/api/endpoints/eventApi'
import { eventApi } from '../../../../core/api/endpoints/eventApi'

const PAGE_SIZE = 4

function formatDateRange(startsAt: string, endsAt: string) {
  const start = new Date(startsAt)
  const end = new Date(endsAt)
  const startLabel = start.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
  const endLabel = end.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
  return startLabel === endLabel ? startLabel : `${startLabel} - ${endLabel}`
}

/**
 * Compact dashboard card previewing the next few upcoming events, backed by the real
 * GET /api/events?upcomingOnly=true endpoint. Replaces the old EventsPreviewWidget mock now that
 * the Event Management module has shipped - see EventsPage for the full list/manage experience
 * this card links out to.
 */
export function UpcomingEventsWidget() {
  const [events, setEvents] = useState<Event[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)

  useEffect(() => {
    let cancelled = false
    eventApi
      .getEvents({ upcomingOnly: true, pageSize: PAGE_SIZE })
      .then((result) => {
        if (cancelled) return
        setEvents(result.items)
        setTotalCount(result.totalCount)
      })
      .catch(() => {
        if (!cancelled) setError(true)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <div className="card h-full">
      <div className="card-header">
        <h6 className="card-title flex items-center gap-2">
          <LuCalendarClock className="size-4 shrink-0" />
          Upcoming Events
        </h6>
        <Link to="/events" className="text-xs text-primary hover:underline shrink-0">
          View all
        </Link>
      </div>
      <div className="card-body flex flex-col gap-4">
        {error ? (
          <p className="text-sm text-danger">Could not load upcoming events.</p>
        ) : loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <>
            <StatTile icon={LuCalendarClock} label="Upcoming events" value={totalCount} accent="bg-primary/15 text-primary" />

            {events.length === 0 ? (
              <p className="text-sm text-default-500">No upcoming events scheduled.</p>
            ) : (
              <ul className="flex flex-col">
                {events.map((event) => (
                  <li
                    key={event.id}
                    className="flex items-start justify-between gap-3 py-2 border-b border-default-200 last:border-b-0"
                  >
                    <Link to="/events" className="flex flex-col min-w-0 hover:underline">
                      <span className="text-sm text-default-700 font-medium truncate">{event.title}</span>
                      {(event.venue || event.chapter) && (
                        <span className="flex items-center gap-1 text-xs text-default-400">
                          <LuMapPin className="size-3 shrink-0" />
                          <span className="truncate">{event.venue ?? event.chapter}</span>
                        </span>
                      )}
                    </Link>
                    <span className="text-xs text-default-500 shrink-0 whitespace-nowrap">
                      {formatDateRange(event.startsAt, event.endsAt)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </>
        )}
      </div>
    </div>
  )
}
