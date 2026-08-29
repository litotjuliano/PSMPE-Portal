import { useEffect, useState } from 'react'
import type { Event } from '../../../core/api/endpoints/eventApi'
import { EventMode, type EventModeValue, eventApi } from '../../../core/api/endpoints/eventApi'
import { describeError } from '../../../core/utils/apiError'
import { StandardButton } from '../components/shared/StandardButton'

interface EventRegisterModalProps {
  event: Event
  onClose: () => void
  onRegistered: () => void
}

function feeForMode(event: Event, mode: EventModeValue): number {
  return mode === EventMode.Onsite ? event.feeOnsite : event.feeOnline
}

/** Member-facing: shows the event's detail (poster, type, hours, objectives, sessions with their
 *  effective venue), lets the member pick a modality (fee and CPD units update live for whichever
 *  is selected), registers, then optionally submits payment proof right away (the member can also
 *  come back to it later from My CPD - registering alone is enough to hold the Registered row). */
export function EventRegisterModal({ event, onClose, onRegistered }: EventRegisterModalProps) {
  const [mode, setMode] = useState<EventModeValue>(EventMode.Onsite)
  const [amount, setAmount] = useState(feeForMode(event, EventMode.Onsite).toString())
  const [referenceNo, setReferenceNo] = useState('')
  const [paidOn, setPaidOn] = useState(new Date().toISOString().slice(0, 10))
  const [proofFile, setProofFile] = useState<File | null>(null)
  const [registrationId, setRegistrationId] = useState<string | null>(null)
  const [posterUrl, setPosterUrl] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Same Escape-to-close/backdrop-click shell as ConfirmationModal, LogDetailsModal, etc.
  useEffect(() => {
    const handleKeyDown = (keyEvent: KeyboardEvent) => {
      if (keyEvent.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  // Loads the poster (if any) for preview.
  useEffect(() => {
    if (!event.hasPoster) return
    let cancelled = false
    eventApi.getPosterUrl(event.id).then((url) => {
      if (!cancelled) setPosterUrl(url)
    })
    return () => {
      cancelled = true
    }
  }, [event.id, event.hasPoster])

  // Revokes the blob URL on unmount (or if it's ever replaced) - same pattern as posterPreviewUrl
  // in EventFormModal.tsx / photoPreviewUrl in MembershipApplicationWizardCard.tsx.
  useEffect(() => {
    return () => {
      if (posterUrl) URL.revokeObjectURL(posterUrl)
    }
  }, [posterUrl])

  // Keeps the amount field in sync with whichever modality is currently selected, but only before
  // the member has registered - once registrationId is set, the amount field becomes the member's
  // own editable payment declaration and should stop tracking the radio selection.
  useEffect(() => {
    if (!registrationId) {
      setAmount(feeForMode(event, mode).toString())
    }
  }, [event, mode, registrationId])

  const handleRegister = async () => {
    setSaving(true)
    setError(null)
    try {
      const registration = await eventApi.register(event.id, mode)
      setRegistrationId(registration.id)
      if (feeForMode(event, mode) <= 0) {
        onRegistered()
      }
    } catch (err) {
      setError(describeError(err, 'Could not register for this event.'))
    } finally {
      setSaving(false)
    }
  }

  const handleSubmitPayment = async () => {
    if (!registrationId) return
    setSaving(true)
    setError(null)
    try {
      const payment = await eventApi.submitPayment(registrationId, { amount: Number(amount), referenceNo: referenceNo || null, paidOn })
      if (proofFile) {
        await eventApi.uploadPaymentProof(payment.id, proofFile)
      }
      onRegistered()
    } catch (err) {
      setError(describeError(err, 'Could not submit your payment.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-100 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />
      <div className="relative card w-full max-w-md max-h-[90vh] overflow-y-auto">
        <div className="card-header">
          <h6 className="card-title">Register for {event.title}</h6>
        </div>
        <div className="card-body flex flex-col gap-3">
          {error && <p className="text-sm text-danger">{error}</p>}

          {posterUrl && <img src={posterUrl} alt={event.title} className="w-full h-32 object-cover rounded-md" />}
          {event.type && <p className="text-xs text-default-500">{event.type}</p>}
          {event.hours !== null && <p className="text-xs text-default-500">{event.hours} PRC hour(s)</p>}
          {event.objectives && <p className="text-sm text-default-600">{event.objectives}</p>}
          {event.sessions.length > 0 && (
            <div className="text-xs text-default-500 flex flex-col gap-0.5">
              {event.sessions.map((s) => (
                <div key={s.id}>
                  {s.title} — {s.venue ?? event.venue ?? 'Venue TBA'}
                </div>
              ))}
            </div>
          )}

          {!registrationId ? (
            <>
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" name="eventMode" className="form-radio" checked={mode === EventMode.Onsite} onChange={() => setMode(EventMode.Onsite)} />
                Onsite {event.cpdUnitsOnsite !== null ? `(${event.cpdUnitsOnsite} CPD units${event.cpdCodeOnsite ? `, ${event.cpdCodeOnsite}` : ''})` : '(CPD units: TBD)'}
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" name="eventMode" className="form-radio" checked={mode === EventMode.Online} onChange={() => setMode(EventMode.Online)} />
                Online {event.cpdUnitsOnline !== null ? `(${event.cpdUnitsOnline} CPD units${event.cpdCodeOnline ? `, ${event.cpdCodeOnline}` : ''})` : '(CPD units: TBD)'}
              </label>
              <p className="text-sm text-default-600">
                Fee: {feeForMode(event, mode) > 0 ? `PHP ${feeForMode(event, mode).toFixed(2)}` : 'Free'}
              </p>
            </>
          ) : (
            <>
              <p className="text-sm text-default-600">You're registered. Submit your payment proof to move to verification:</p>
              <input
                type="number"
                min="0"
                step="0.01"
                className="form-input"
                placeholder="Amount"
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
              />
              <input className="form-input" placeholder="Reference No." value={referenceNo} onChange={(e) => setReferenceNo(e.target.value)} />
              <input
                type="date"
                className="form-input"
                max={new Date().toISOString().slice(0, 10)}
                value={paidOn}
                onChange={(e) => setPaidOn(e.target.value)}
              />
              <input type="file" accept="image/*,.pdf" className="text-sm" onChange={(e) => setProofFile(e.target.files?.[0] ?? null)} />
            </>
          )}
        </div>
        <div className="card-footer flex justify-end gap-2">
          <StandardButton variant="secondary" onClick={onClose} disabled={saving}>
            Cancel
          </StandardButton>
          {!registrationId ? (
            <StandardButton onClick={handleRegister} loading={saving} loadingLabel="Registering…">
              Register
            </StandardButton>
          ) : (
            <StandardButton onClick={handleSubmitPayment} loading={saving} loadingLabel="Submitting…">
              Submit Payment
            </StandardButton>
          )}
        </div>
      </div>
    </div>
  )
}
