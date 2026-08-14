import { LuCalendarClock, LuMapPin, LuSparkles } from 'react-icons/lu'
import { StatTile } from '../shared/StatTile'

/**
 * Placeholder preview of the future Event Management module. There is no Event entity, endpoint,
 * or controller anywhere in this codebase yet - every "upcoming event" below is fictional,
 * hardcoded sample content shown purely to preview the feature to prospective and current members.
 * Do not wire this to any API. Replace/delete this whole component once the real module ships.
 */

interface MockEvent {
  name: string
  date: string
  location: string
}

const MOCK_EVENTS: MockEvent[] = [
  { name: '2026 PSMPE National Convention', date: 'Sep 18-19, 2026', location: 'SMX Convention Center, Pasay City' },
  { name: 'CPD Seminar: National Plumbing Code Updates', date: 'Aug 29, 2026', location: 'NCR Chapter' },
  { name: 'Cebu Chapter General Membership Meeting', date: 'Sep 5, 2026', location: 'Cebu Chapter' },
  { name: 'Water Sanitation & Cross-Connection Control Workshop', date: 'Oct 3, 2026', location: 'Davao Chapter' },
]

/** Dummy dashboard card previewing the not-yet-built Event Management module. */
export function EventsPreviewWidget() {
  return (
    <div className="card h-full border-2 border-dashed border-warning/40 bg-warning/5 dark:bg-warning/10">
      <div className="card-header">
        <h6 className="card-title flex items-center gap-2">
          <LuCalendarClock className="size-4 shrink-0" />
          Event Management
        </h6>
        <span className="inline-flex items-center gap-1 py-0.5 px-2.5 rounded-full text-xs font-semibold bg-white text-warning shrink-0">
          <LuSparkles className="size-3" />
          Preview · Coming Soon
        </span>
      </div>
      <div className="card-body flex flex-col gap-4">
        <StatTile icon={LuCalendarClock} label="Upcoming events" value={MOCK_EVENTS.length} accent="bg-warning/15 text-warning" />

        <ul className="flex flex-col">
          {MOCK_EVENTS.map((event) => (
            <li
              key={event.name}
              className="flex items-start justify-between gap-3 py-2 border-b border-dashed border-default-200 last:border-b-0"
            >
              <span className="flex flex-col">
                <span className="text-sm text-default-700 font-medium">{event.name}</span>
                <span className="flex items-center gap-1 text-xs text-default-400">
                  <LuMapPin className="size-3 shrink-0" />
                  {event.location}
                </span>
              </span>
              <span className="text-xs text-default-500 shrink-0 whitespace-nowrap">{event.date}</span>
            </li>
          ))}
        </ul>

        <p className="text-xs text-default-500 rounded-lg bg-default-100 px-3 py-2">
          Sample content only - the Event Management module is not yet available. Nothing above is real.
        </p>
      </div>
    </div>
  )
}
