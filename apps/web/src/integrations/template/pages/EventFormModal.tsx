import { useEffect, useState } from 'react'
import type { Event, EventSessionInput } from '../../../core/api/endpoints/eventApi'
import { eventApi } from '../../../core/api/endpoints/eventApi'
import { Chapters } from '../../../core/types/member'
import { describeError } from '../../../core/utils/apiError'
import { StandardButton } from '../components/shared/StandardButton'

interface EventFormModalProps {
  event: Event | null
  mode: 'create' | 'edit'
  onClose: () => void
  onSaved: () => void
}

function toSessionInputs(event: Event | null): EventSessionInput[] {
  return event?.sessions.map((s) => ({ id: s.id, title: s.title, startsAt: s.startsAt, endsAt: s.endsAt, order: s.order })) ?? []
}

/** Admin-only event create/edit, including session (lecture) management and setting each
 *  modality's CPD units - see EventService.UpdateAsync's session reconciliation on the backend. */
export function EventFormModal({ event, mode, onClose, onSaved }: EventFormModalProps) {
  const [title, setTitle] = useState(event?.title ?? '')
  const [description, setDescription] = useState(event?.description ?? '')
  const [chapter, setChapter] = useState(event?.chapter ?? '')
  const [venue, setVenue] = useState(event?.venue ?? '')
  const [startsAt, setStartsAt] = useState(event?.startsAt.slice(0, 16) ?? '')
  const [endsAt, setEndsAt] = useState(event?.endsAt.slice(0, 16) ?? '')
  const [capacity, setCapacity] = useState(event?.capacity?.toString() ?? '')
  const [fee, setFee] = useState(event?.fee.toString() ?? '0')
  const [cpdUnitsOnsite, setCpdUnitsOnsite] = useState(event?.cpdUnitsOnsite?.toString() ?? '')
  const [cpdUnitsOnline, setCpdUnitsOnline] = useState(event?.cpdUnitsOnline?.toString() ?? '')
  const [sessions, setSessions] = useState<EventSessionInput[]>(toSessionInputs(event))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setSessions(toSessionInputs(event))
  }, [event])

  // Same Escape-to-close/backdrop-click shell as ConfirmationModal, LogDetailsModal, etc.
  useEffect(() => {
    const handleKeyDown = (keyEvent: KeyboardEvent) => {
      if (keyEvent.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  const updateSession = (index: number, patch: Partial<EventSessionInput>) => {
    setSessions((prev) => prev.map((s, i) => (i === index ? { ...s, ...patch } : s)))
  }

  const addSession = () => {
    setSessions((prev) => [...prev, { id: null, title: '', startsAt, endsAt, order: prev.length + 1 }])
  }

  const removeSession = (index: number) => {
    setSessions((prev) => prev.filter((_, i) => i !== index))
  }

  const handleSubmit = async () => {
    setSaving(true)
    setError(null)
    try {
      const basePayload = {
        title,
        description: description || null,
        chapter: chapter || null,
        venue: venue || null,
        startsAt: new Date(startsAt).toISOString(),
        endsAt: new Date(endsAt).toISOString(),
        capacity: capacity ? Number(capacity) : null,
        fee: Number(fee),
      }

      if (mode === 'create') {
        await eventApi.createEvent(basePayload)
      } else if (event) {
        await eventApi.updateEvent(event.id, {
          ...basePayload,
          cpdUnitsOnsite: cpdUnitsOnsite ? Number(cpdUnitsOnsite) : null,
          cpdUnitsOnline: cpdUnitsOnline ? Number(cpdUnitsOnline) : null,
          sessions,
        })
      }
      onSaved()
    } catch (err) {
      setError(describeError(err, 'Could not save this event.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-100 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />
      <div className="relative card w-full max-w-2xl max-h-[90vh] overflow-y-auto">
        <div className="card-header">
          <h6 className="card-title">{mode === 'create' ? 'New Event' : 'Edit Event'}</h6>
        </div>
        <div className="card-body flex flex-col gap-3">
          {error && <p className="text-sm text-danger">{error}</p>}
          <input className="form-input" placeholder="Title" value={title} onChange={(e) => setTitle(e.target.value)} />
          <textarea
            className="form-input"
            placeholder="Description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
          <div className="grid grid-cols-2 gap-3">
            <select className="form-input" value={chapter} onChange={(e) => setChapter(e.target.value)}>
              <option value="">National (all chapters)</option>
              {Object.values(Chapters).map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
            <input className="form-input" placeholder="Venue" value={venue} onChange={(e) => setVenue(e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <input type="datetime-local" className="form-input" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
            <input type="datetime-local" className="form-input" value={endsAt} onChange={(e) => setEndsAt(e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <input
              type="number"
              className="form-input"
              placeholder="Capacity"
              value={capacity}
              onChange={(e) => setCapacity(e.target.value)}
            />
            <input type="number" className="form-input" placeholder="Fee" value={fee} onChange={(e) => setFee(e.target.value)} />
          </div>

          {mode === 'edit' && (
            <>
              <div className="grid grid-cols-2 gap-3">
                <input
                  type="number"
                  step="0.01"
                  className="form-input"
                  placeholder="CPD Units (Onsite) - blank for TBD"
                  value={cpdUnitsOnsite}
                  onChange={(e) => setCpdUnitsOnsite(e.target.value)}
                />
                <input
                  type="number"
                  step="0.01"
                  className="form-input"
                  placeholder="CPD Units (Online) - blank for TBD"
                  value={cpdUnitsOnline}
                  onChange={(e) => setCpdUnitsOnline(e.target.value)}
                />
              </div>

              <div className="border-t border-default-200 pt-3">
                <div className="flex items-center justify-between mb-2">
                  <h6 className="text-sm font-semibold">Sessions / Lectures</h6>
                  <StandardButton variant="secondary" size="sm" onClick={addSession}>
                    Add session
                  </StandardButton>
                </div>
                {sessions.map((session, index) => (
                  <div key={session.id ?? `new-${index}`} className="grid grid-cols-[1fr_auto_auto_auto] gap-2 mb-2 items-center">
                    <input
                      className="form-input"
                      placeholder="Session title"
                      value={session.title}
                      onChange={(e) => updateSession(index, { title: e.target.value })}
                    />
                    <input
                      type="datetime-local"
                      className="form-input"
                      value={session.startsAt.slice(0, 16)}
                      onChange={(e) => updateSession(index, { startsAt: new Date(e.target.value).toISOString() })}
                    />
                    <input
                      type="datetime-local"
                      className="form-input"
                      value={session.endsAt.slice(0, 16)}
                      onChange={(e) => updateSession(index, { endsAt: new Date(e.target.value).toISOString() })}
                    />
                    <StandardButton variant="danger" size="sm" onClick={() => removeSession(index)}>
                      Remove
                    </StandardButton>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>
        <div className="card-footer flex justify-end gap-2">
          <StandardButton variant="secondary" onClick={onClose} disabled={saving}>
            Cancel
          </StandardButton>
          <StandardButton onClick={handleSubmit} loading={saving} loadingLabel="Saving…">
            Save
          </StandardButton>
        </div>
      </div>
    </div>
  )
}
