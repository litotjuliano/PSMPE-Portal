import { useCallback, useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import type { EventRoster } from '../api/endpoints/eventApi'
import { EventRegistrationStatus, eventApi } from '../api/endpoints/eventApi'
import { describeError } from '../utils/apiError'
import { EventRosterTable, PageBreadcrumb, PageMeta } from '../../integrations/template'

/** Mirrors the backend's `RecordAttendanceAsync` gate (EventService.Attendance.cs): it validates
 *  the ENTIRE batch before writing anything and fails the whole request if even one registrant's
 *  status isn't one of these - which is the normal state of any in-progress roster (most
 *  registrants haven't had payment verified yet). Attendance can only be submitted for the
 *  registrants who are actually eligible; everyone else's (always-empty, since their checkboxes
 *  are disabled) entry is left out of the payload rather than sent and rejected. */
const ATTENDANCE_ELIGIBLE_STATUSES = new Set<string>([
  EventRegistrationStatus.PaymentVerified,
  EventRegistrationStatus.Attended,
  EventRegistrationStatus.EvaluationSubmitted,
])

export function EventRosterPage() {
  const { id } = useParams<{ id: string }>()
  const [roster, setRoster] = useState<EventRoster | null>(null)
  const [pendingAttendance, setPendingAttendance] = useState<Record<string, Set<string>>>({})
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false)
  const [loading, setLoading] = useState(true)
  const [savingAttendance, setSavingAttendance] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchRoster = useCallback(async () => {
    if (!id) return
    const result = await eventApi.getRoster(id)
    setRoster(result)
    setPendingAttendance(
      Object.fromEntries(result.registrants.map((r) => [r.registrationId, new Set(r.attendedSessionIds)])),
    )
    // A fresh fetch is by definition the saved baseline - nothing pending against it yet.
    setHasUnsavedChanges(false)
  }, [id])

  useEffect(() => {
    setLoading(true)
    setError(null)
    fetchRoster()
      .catch((err) => setError(describeError(err, 'Could not load the roster.')))
      .finally(() => setLoading(false))
  }, [fetchRoster])

  const handleToggleSession = (registrationId: string, sessionId: string) => {
    setPendingAttendance((prev) => {
      const next = new Set(prev[registrationId] ?? [])
      if (next.has(sessionId)) next.delete(sessionId)
      else next.add(sessionId)
      return { ...prev, [registrationId]: next }
    })
    setHasUnsavedChanges(true)
  }

  const handleSaveAttendance = async () => {
    if (!id || !roster) return
    setSavingAttendance(true)
    setError(null)
    try {
      // Only send registrants the backend will actually accept - see ATTENDANCE_ELIGIBLE_STATUSES.
      // Sending the full pendingAttendance map (seeded from every registrant on the roster) makes
      // the backend reject the whole batch the moment it hits one PaymentSubmitted/Rejected/etc.
      // registrant, even though their checkboxes are disabled and never had anything toggled.
      const eligibleIds = new Set(
        roster.registrants.filter((r) => ATTENDANCE_ELIGIBLE_STATUSES.has(r.status)).map((r) => r.registrationId),
      )
      const registrants = Object.entries(pendingAttendance)
        .filter(([registrationId]) => eligibleIds.has(registrationId))
        .map(([registrationId, sessionIds]) => ({
          registrationId,
          sessionIds: [...sessionIds],
        }))
      await eventApi.recordAttendance(id, registrants)
      await fetchRoster()
    } catch (err) {
      setError(describeError(err, 'Could not save attendance.'))
    } finally {
      setSavingAttendance(false)
    }
  }

  const handleRecordCashPayment = async (registrationId: string, amount: number) => {
    setError(null)
    try {
      await eventApi.recordCashPayment(registrationId, amount)
      await fetchRoster()
    } catch (err) {
      setError(describeError(err, 'Could not record this cash payment.'))
    }
  }

  return (
    <>
      <PageMeta title="Event Roster" />
      <main>
        <PageBreadcrumb title={roster ? `Roster: ${roster.eventTitle}` : 'Roster'} />

        {error && <p className="text-sm text-danger mb-4">{error}</p>}

        {loading || !roster ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <EventRosterTable
            sessions={roster.sessions}
            registrants={roster.registrants}
            pendingAttendance={pendingAttendance}
            onToggleSession={handleToggleSession}
            onSaveAttendance={handleSaveAttendance}
            savingAttendance={savingAttendance}
            hasUnsavedChanges={hasUnsavedChanges}
            onRecordCashPayment={handleRecordCashPayment}
          />
        )}
      </main>
    </>
  )
}
