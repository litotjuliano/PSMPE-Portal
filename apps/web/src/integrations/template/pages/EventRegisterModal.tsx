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

/** Member-facing: pick a modality, register, then optionally submit payment proof right away
 *  (the member can also come back to it later from My CPD - registering alone is enough to hold
 *  the Registered row). */
export function EventRegisterModal({ event, onClose, onRegistered }: EventRegisterModalProps) {
  const [mode, setMode] = useState<EventModeValue>(EventMode.Onsite)
  const [amount, setAmount] = useState(event.fee.toString())
  const [referenceNo, setReferenceNo] = useState('')
  const [paidOn, setPaidOn] = useState(new Date().toISOString().slice(0, 10))
  const [proofFile, setProofFile] = useState<File | null>(null)
  const [registrationId, setRegistrationId] = useState<string | null>(null)
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

  const handleRegister = async () => {
    setSaving(true)
    setError(null)
    try {
      const registration = await eventApi.register(event.id, mode)
      setRegistrationId(registration.id)
      if (event.fee <= 0) {
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
      <div className="relative card w-full max-w-md">
        <div className="card-header">
          <h6 className="card-title">Register for {event.title}</h6>
        </div>
        <div className="card-body flex flex-col gap-3">
          {error && <p className="text-sm text-danger">{error}</p>}

          {!registrationId ? (
            <>
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" name="eventMode" className="form-radio" checked={mode === EventMode.Onsite} onChange={() => setMode(EventMode.Onsite)} />
                Onsite {event.cpdUnitsOnsite !== null ? `(${event.cpdUnitsOnsite} CPD units)` : '(CPD units: TBD)'}
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" name="eventMode" className="form-radio" checked={mode === EventMode.Online} onChange={() => setMode(EventMode.Online)} />
                Online {event.cpdUnitsOnline !== null ? `(${event.cpdUnitsOnline} CPD units)` : '(CPD units: TBD)'}
              </label>
              <p className="text-sm text-default-600">Fee: {event.fee > 0 ? `PHP ${event.fee.toFixed(2)}` : 'Free'}</p>
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
