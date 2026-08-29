import { useCallback, useEffect, useState } from 'react'
import { LuCalendarClock, LuMapPin } from 'react-icons/lu'
import type { Event } from '../../../../core/api/endpoints/eventApi'
import { eventApi } from '../../../../core/api/endpoints/eventApi'
import { useAuth } from '../../../../core/auth/useAuth'
import { Roles } from '../../../../core/types/auth'
import { StandardButton } from '../shared/StandardButton'
import { EventFormModal } from '../../pages/EventFormModal'
import { EventRegisterModal } from '../../pages/EventRegisterModal'

const PAGE_SIZE = 3

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
  const { user } = useAuth()
  const canManageEvents = user?.roles.includes(Roles.Admin) || user?.roles.includes(Roles.SuperAdmin) || false
  const isMember = user?.roles.includes(Roles.Member) || false

  const [events, setEvents] = useState<Event[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)
  const [posterUrls, setPosterUrls] = useState<Record<string, string>>({})

  const [formEvent, setFormEvent] = useState<Event | null>(null)
  const [registeringEvent, setRegisteringEvent] = useState<Event | null>(null)

  const fetchEvents = useCallback((isStale: () => boolean = () => false) => {
    return eventApi.getEvents({ upcomingOnly: true, pageSize: PAGE_SIZE }).then((result) => {
      if (isStale()) return
      setEvents(result.items)
    })
  }, [])

  useEffect(() => {
    let cancelled = false
    fetchEvents(() => cancelled)
      .catch(() => {
        if (!cancelled) setError(true)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [fetchEvents])

  // Mirrors EventsPage's handleSelectEvent: an admin/manager gets the edit form, a member gets
  // the register modal - same click, same event, different modal depending on what the viewer
  // can actually do with it.
  const handleSelectEvent = (event: Event) => {
    if (canManageEvents) {
      setFormEvent(event)
    } else if (isMember) {
      setRegisteringEvent(event)
    }
  }

  // Fetches a poster thumbnail for each event that has one, keyed by event id so a slow/failed
  // fetch for one event doesn't block the others. Revokes every blob URL on cleanup/re-run,
  // matching the pattern established in EventFormModal.tsx/EventRegisterModal.tsx.
  useEffect(() => {
    let cancelled = false
    const urls: Record<string, string> = {}
    Promise.all(
      events
        .filter((event) => event.hasPoster)
        .map((event) =>
          eventApi.getPosterUrl(event.id).then((url) => {
            if (url) urls[event.id] = url
          }),
        ),
    ).then(() => {
      if (!cancelled) setPosterUrls(urls)
    })
    return () => {
      cancelled = true
      Object.values(urls).forEach((url) => URL.revokeObjectURL(url))
    }
  }, [events])

  return (
    <div className="card h-full">
      <div className="card-header">
        <h6 className="card-title flex items-center gap-2">
          <LuCalendarClock className="size-4 shrink-0" />
          Upcoming Events
        </h6>
      </div>
      <div className="card-body flex flex-col gap-4">
        {error ? (
          <p className="text-sm text-danger">Could not load upcoming events.</p>
        ) : loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <>
            {events.length === 0 ? (
              <p className="text-sm text-default-500">No upcoming events scheduled.</p>
            ) : (
              <ul className="flex flex-col gap-3">
                {events.map((event) => (
                  <li
                    key={event.id}
                    className="flex flex-col gap-2 pb-3 border-b border-default-200 last:border-b-0 last:pb-0 cursor-pointer hover:opacity-90"
                    onClick={() => handleSelectEvent(event)}
                  >
                    {posterUrls[event.id] && (
                      <img src={posterUrls[event.id]} alt="" className="w-full h-auto rounded-md" />
                    )}
                    <div className="flex items-start justify-between gap-3">
                      <div className="flex flex-col min-w-0">
                        <span className="text-sm text-default-700 font-medium line-clamp-2">{event.title}</span>
                        {(event.venue || event.chapter) && (
                          <span className="flex items-center gap-1 text-xs text-default-400">
                            <LuMapPin className="size-3 shrink-0" />
                            <span className="truncate">{event.venue ?? event.chapter}</span>
                          </span>
                        )}
                      </div>
                      <span className="text-xs text-default-500 shrink-0 whitespace-nowrap">
                        {formatDateRange(event.startsAt, event.endsAt)}
                      </span>
                    </div>
                  </li>
                ))}
              </ul>
            )}

            <StandardButton to="/events" variant="primary" className="w-full justify-center">
              More Events
            </StandardButton>
          </>
        )}
      </div>

      {formEvent && (
        <EventFormModal
          event={formEvent}
          mode="edit"
          onClose={() => setFormEvent(null)}
          onSaved={() => {
            setFormEvent(null)
            fetchEvents()
          }}
        />
      )}

      {registeringEvent && (
        <EventRegisterModal
          event={registeringEvent}
          onClose={() => setRegisteringEvent(null)}
          onRegistered={() => {
            setRegisteringEvent(null)
            fetchEvents()
          }}
        />
      )}
    </div>
  )
}
