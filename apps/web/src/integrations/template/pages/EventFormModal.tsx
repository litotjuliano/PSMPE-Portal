import { useEffect, useState } from 'react'
import type { Event, EventSessionInput } from '../../../core/api/endpoints/eventApi'
import { EventTypes, eventApi } from '../../../core/api/endpoints/eventApi'
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
  return event?.sessions.map((s) => ({ id: s.id, title: s.title, startsAt: s.startsAt, endsAt: s.endsAt, order: s.order, venue: s.venue })) ?? []
}

/** Admin-only event create/edit, including session (lecture) management, each modality's fee/CPD
 *  units/accreditation code, the poster image, and the descriptive fields (Type, Hours, Objectives) -
 *  see EventService.UpdateAsync's session reconciliation on the backend. */
export function EventFormModal({ event, mode, onClose, onSaved }: EventFormModalProps) {
  const [title, setTitle] = useState(event?.title ?? '')
  const [description, setDescription] = useState(event?.description ?? '')
  const [objectives, setObjectives] = useState(event?.objectives ?? '')
  const [type, setType] = useState(event?.type ?? '')
  const [chapter, setChapter] = useState(event?.chapter ?? '')
  const [venue, setVenue] = useState(event?.venue ?? '')
  const [startsAt, setStartsAt] = useState(event?.startsAt.slice(0, 16) ?? '')
  const [endsAt, setEndsAt] = useState(event?.endsAt.slice(0, 16) ?? '')
  const [hours, setHours] = useState(event?.hours?.toString() ?? '')
  const [capacity, setCapacity] = useState(event?.capacity?.toString() ?? '')
  const [feeOnsite, setFeeOnsite] = useState(event?.feeOnsite.toString() ?? '0')
  const [feeOnline, setFeeOnline] = useState(event?.feeOnline.toString() ?? '0')
  const [cpdUnitsOnsite, setCpdUnitsOnsite] = useState(event?.cpdUnitsOnsite?.toString() ?? '')
  const [cpdUnitsOnline, setCpdUnitsOnline] = useState(event?.cpdUnitsOnline?.toString() ?? '')
  const [cpdCodeOnsite, setCpdCodeOnsite] = useState(event?.cpdCodeOnsite ?? '')
  const [cpdCodeOnline, setCpdCodeOnline] = useState(event?.cpdCodeOnline ?? '')
  const [sessions, setSessions] = useState<EventSessionInput[]>(toSessionInputs(event))
  const [posterFile, setPosterFile] = useState<File | null>(null)
  const [posterPreviewUrl, setPosterPreviewUrl] = useState<string | null>(null)
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

  // Loads the existing poster (if any) for preview when editing - a freshly-chosen posterFile
  // (handled by handlePosterFileChange below) takes priority over this fetched preview.
  useEffect(() => {
    if (!event?.hasPoster) return
    let cancelled = false
    eventApi.getPosterUrl(event.id).then((url) => {
      if (!cancelled) setPosterPreviewUrl(url)
    })
    return () => {
      cancelled = true
    }
  }, [event])

  // Revokes the previous blob URL whenever it's replaced (a freshly-picked file superseding a
  // fetched preview, or the reverse) and on unmount - same pattern as photoPreviewUrl in
  // MembershipApplicationWizardCard.tsx / MemberFormCard.tsx.
  useEffect(() => {
    return () => {
      if (posterPreviewUrl) URL.revokeObjectURL(posterPreviewUrl)
    }
  }, [posterPreviewUrl])

  const handlePosterFileChange = (file: File | null) => {
    setPosterFile(file)
    if (file) {
      // Instant local preview - no need to wait for a round trip to see the picked poster.
      if (posterPreviewUrl) URL.revokeObjectURL(posterPreviewUrl)
      setPosterPreviewUrl(URL.createObjectURL(file))
    }
  }

  const updateSession = (index: number, patch: Partial<EventSessionInput>) => {
    setSessions((prev) => prev.map((s, i) => (i === index ? { ...s, ...patch } : s)))
  }

  const addSession = () => {
    setSessions((prev) => [...prev, { id: null, title: '', startsAt, endsAt, order: prev.length + 1, venue: null }])
  }

  const removeSession = (index: number) => {
    setSessions((prev) => prev.filter((_, i) => i !== index))
  }

  const handleSubmit = async () => {
    setSaving(true)
    setError(null)

    const basePayload = {
      title,
      description: description || null,
      chapter: chapter || null,
      venue: venue || null,
      startsAt: new Date(startsAt).toISOString(),
      endsAt: new Date(endsAt).toISOString(),
      capacity: capacity ? Number(capacity) : null,
      feeOnsite: Number(feeOnsite),
      feeOnline: Number(feeOnline),
      type: type || null,
      hours: hours ? Number(hours) : null,
      objectives: objectives || null,
    }

    let savedEventId = event?.id ?? null
    try {
      if (mode === 'create') {
        const created = await eventApi.createEvent(basePayload)
        savedEventId = created.id
      } else if (event) {
        await eventApi.updateEvent(event.id, {
          ...basePayload,
          cpdUnitsOnsite: cpdUnitsOnsite ? Number(cpdUnitsOnsite) : null,
          cpdUnitsOnline: cpdUnitsOnline ? Number(cpdUnitsOnline) : null,
          cpdCodeOnsite: cpdCodeOnsite || null,
          cpdCodeOnline: cpdCodeOnline || null,
          sessions,
        })
      }
    } catch (err) {
      // Nothing was persisted - safe to let the admin retry the whole form as before.
      setError(describeError(err, 'Could not save this event.'))
      setSaving(false)
      return
    }

    if (posterFile && savedEventId) {
      try {
        await eventApi.uploadPoster(savedEventId, posterFile)
      } catch (err) {
        // The event itself is already persisted at this point - closing via onSaved() (rather than
        // leaving the form open) avoids the admin re-submitting and creating a duplicate event. That
        // same onSaved() unmounts this modal right away, so an inline `error` banner would never be
        // seen - an alert is the only way to actually surface this to the admin.
        window.alert(
          `Event saved, but the poster upload failed: ${describeError(err, 'an unknown error occurred')}. You can try uploading it again from Edit.`,
        )
        setSaving(false)
        onSaved()
        return
      }
    }

    setSaving(false)
    onSaved()
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
          <textarea
            className="form-input"
            placeholder="Objectives"
            value={objectives}
            onChange={(e) => setObjectives(e.target.value)}
          />
          <div className="grid grid-cols-2 gap-3">
            <select className="form-input" value={type} onChange={(e) => setType(e.target.value)}>
              <option value="">No type set</option>
              {Object.values(EventTypes).map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
            <input
              type="number"
              step="0.01"
              className="form-input"
              placeholder="Hours (PRC-declared)"
              value={hours}
              onChange={(e) => setHours(e.target.value)}
            />
          </div>
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
          <div className="grid grid-cols-3 gap-3">
            <input
              type="number"
              className="form-input"
              placeholder="Capacity"
              value={capacity}
              onChange={(e) => setCapacity(e.target.value)}
            />
            <input
              type="number"
              className="form-input"
              placeholder="Fee (Onsite)"
              value={feeOnsite}
              onChange={(e) => setFeeOnsite(e.target.value)}
            />
            <input
              type="number"
              className="form-input"
              placeholder="Fee (Online)"
              value={feeOnline}
              onChange={(e) => setFeeOnline(e.target.value)}
            />
          </div>

          <div>
            <label className="text-sm text-default-600 block mb-1">Poster / banner image</label>
            {posterPreviewUrl && (
              <img src={posterPreviewUrl} alt="Poster preview" className="w-full h-32 object-cover rounded-md mb-2" />
            )}
            <input
              type="file"
              accept="image/jpeg,image/png"
              className="text-sm"
              onChange={(e) => handlePosterFileChange(e.target.files?.[0] ?? null)}
            />
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
              <div className="grid grid-cols-2 gap-3">
                <input
                  className="form-input"
                  placeholder="PRC Accreditation Code (Onsite)"
                  value={cpdCodeOnsite}
                  onChange={(e) => setCpdCodeOnsite(e.target.value)}
                />
                <input
                  className="form-input"
                  placeholder="PRC Accreditation Code (Online)"
                  value={cpdCodeOnline}
                  onChange={(e) => setCpdCodeOnline(e.target.value)}
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
                  <div key={session.id ?? `new-${index}`} className="grid grid-cols-[1fr_auto_auto_1fr_auto] gap-2 mb-2 items-center">
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
                    <input
                      className="form-input"
                      placeholder="Venue override (blank = event's venue)"
                      value={session.venue ?? ''}
                      onChange={(e) => updateSession(index, { venue: e.target.value || null })}
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
