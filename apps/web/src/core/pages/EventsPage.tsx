import { useCallback, useEffect, useState } from 'react'
import type { Event } from '../api/endpoints/eventApi'
import { eventApi } from '../api/endpoints/eventApi'
import type { ChapterValue } from '../types/member'
import { describeError } from '../utils/apiError'
import { useAuth } from '../auth/useAuth'
import { Roles } from '../types/auth'
import { EventFormModal, EventRegisterModal, EventsTable, PageBreadcrumb, PageMeta } from '../../integrations/template'

const PAGE_SIZE = 20

export function EventsPage() {
  const { user } = useAuth()
  const canManageEvents = user?.roles.includes(Roles.Admin) || user?.roles.includes(Roles.SuperAdmin) || false
  const isMember = user?.roles.includes(Roles.Member) || false

  const [events, setEvents] = useState<Event[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [chapterFilter, setChapterFilter] = useState<ChapterValue | null>(null)
  const [upcomingOnly, setUpcomingOnly] = useState(true)

  const [formEvent, setFormEvent] = useState<{ event: Event | null; mode: 'create' | 'edit' } | null>(null)
  const [registeringEvent, setRegisteringEvent] = useState<Event | null>(null)

  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchInput)
      setPage(1)
    }, 350)
    return () => clearTimeout(timer)
  }, [searchInput])

  const fetchEvents = useCallback(
    async (isStale: () => boolean = () => false) => {
      const result = await eventApi.getEvents({
        page,
        pageSize: PAGE_SIZE,
        search: search || undefined,
        chapter: chapterFilter ?? undefined,
        upcomingOnly,
      })
      if (isStale()) return
      setEvents(result.items)
      setTotalCount(result.totalCount)
    },
    [page, search, chapterFilter, upcomingOnly],
  )

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    fetchEvents(() => cancelled)
      .catch((err) => {
        if (!cancelled) setError(describeError(err, 'Could not load events. Please try again.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [fetchEvents])

  const handleSelectEvent = (event: Event) => {
    if (canManageEvents) {
      setFormEvent({ event, mode: 'edit' })
    } else if (isMember) {
      setRegisteringEvent(event)
    }
  }

  return (
    <>
      <PageMeta title="Events" />
      <main>
        <PageBreadcrumb title="Events" />

        {error && <p className="text-sm text-danger mb-4">{error}</p>}

        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <EventsTable
            events={events}
            canManageEvents={canManageEvents}
            searchInput={searchInput}
            onSearchInputChange={setSearchInput}
            chapterFilter={chapterFilter}
            onChapterFilterChange={(c) => {
              setChapterFilter(c)
              setPage(1)
            }}
            upcomingOnly={upcomingOnly}
            onUpcomingOnlyChange={(v) => {
              setUpcomingOnly(v)
              setPage(1)
            }}
            page={page}
            pageSize={PAGE_SIZE}
            totalCount={totalCount}
            onPageChange={setPage}
            onNewEvent={() => setFormEvent({ event: null, mode: 'create' })}
            onSelectEvent={handleSelectEvent}
          />
        )}

        {formEvent && (
          <EventFormModal
            event={formEvent.event}
            mode={formEvent.mode}
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
      </main>
    </>
  )
}
