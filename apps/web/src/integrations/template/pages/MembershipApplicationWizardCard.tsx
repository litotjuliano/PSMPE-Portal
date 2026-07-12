import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { isAxiosError } from 'axios'
import { LuUpload, LuUserRound } from 'react-icons/lu'
import { Chapters, MemberTypes } from '../../../core/types/member'
import { uploadApi } from '../../../core/api/endpoints/uploadApi'

// Matches the backend's MemberUploadService caps - checked client-side first so an oversized
// file gets an immediate, friendly message instead of a round trip that risks a raw connection
// reset (Kestrel aborts the connection when a request exceeds its size limit mid-body-read,
// rather than returning a clean 4xx - see MembersController's upload endpoints).
const MaxImageBytes = 24 * 1024 * 1024
const MaxPdfBytes = 2 * 1024 * 1024

function describeUploadError(err: unknown, fallback: string): string {
  if (isAxiosError(err)) {
    if (err.response) {
      const message = (err.response.data as { message?: string } | undefined)?.message
      return message ?? fallback
    }
    return 'Could not reach the server. Please check your connection and try again.'
  }
  return fallback
}

export interface MembershipApplicationState {
  firstName: string
  middleName: string
  lastName: string
  suffix: string
  birthdate: string
  gender: string
  chapter: string
  memberType: string
  prcLicenseNo: string
  address: string
  company: string
  agreedToTerms: boolean
}

interface MembershipApplicationWizardCardProps {
  step: number
  /** Furthest step reached this session - drives which stepper circles are clickable ("completed",
   *  i <= maxStepReached) vs. disabled ("future", i > maxStepReached). See MyProfilePage.tsx. */
  maxStepReached: number
  state: MembershipApplicationState
  onChange: <K extends keyof MembershipApplicationState>(field: K, value: MembershipApplicationState[K]) => void
  onNext: () => void
  onBack: () => void
  onStepClick: (step: number) => void
  onSubmit: (event: FormEvent) => void
  accountEmail: string
  accountDisplayName: string
  error: string | null
  submitting: boolean
  /** True while a stepper-click or Back save is in flight - disables navigation to prevent
   *  overlapping saves from rapid clicks. */
  navigating: boolean
}

const steps = ['Personal Information', 'Contact Information', 'Account Information', 'Additional Information']

function StepIndicator({
  step,
  maxStepReached,
  onStepClick,
  navigating,
}: {
  step: number
  maxStepReached: number
  onStepClick: (step: number) => void
  navigating: boolean
}) {
  return (
    <div className="flex items-center gap-2 mb-6">
      {steps.map((label, i) => {
        const isCurrent = i === step
        const isCompleted = i <= maxStepReached && !isCurrent
        const isFuture = i > maxStepReached
        return (
          <div key={label} className="flex items-center gap-2 flex-1">
            <button
              type="button"
              onClick={() => onStepClick(i)}
              disabled={isCurrent || isFuture || navigating}
              aria-current={isCurrent ? 'step' : undefined}
              aria-label={`Step ${i + 1}: ${label}${isCompleted ? ' (completed - click to edit)' : isFuture ? ' (not yet reached)' : ''}`}
              className={`shrink-0 size-10 rounded-full flex items-center justify-center text-sm font-semibold transition ${
                isCurrent
                  ? 'bg-primary text-white cursor-default'
                  : isCompleted
                    ? 'bg-success text-white hover:bg-success/80 cursor-pointer disabled:cursor-not-allowed disabled:opacity-60'
                    : 'bg-default-150 text-default-400 cursor-not-allowed'
              }`}
            >
              {i + 1}
            </button>
            <span className={`text-xs whitespace-nowrap ${isCurrent ? 'text-default-900 font-medium' : isFuture ? 'text-default-400' : 'text-default-500'}`}>
              {label}
            </span>
            {i < steps.length - 1 && <div className="h-px bg-default-200 flex-1" />}
          </div>
        )
      })}
    </div>
  )
}

export const MembershipApplicationWizardCard = ({
  step,
  maxStepReached,
  state,
  onChange,
  onNext,
  onBack,
  onStepClick,
  onSubmit,
  accountEmail,
  accountDisplayName,
  error,
  submitting,
  navigating,
}: MembershipApplicationWizardCardProps) => {
  const photoInputRef = useRef<HTMLInputElement>(null)
  const prcIdInputRef = useRef<HTMLInputElement>(null)
  const [uploadingPhoto, setUploadingPhoto] = useState(false)
  const [uploadingPrcId, setUploadingPrcId] = useState(false)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [photoPreviewUrl, setPhotoPreviewUrl] = useState<string | null>(null)
  const [hasPrcId, setHasPrcId] = useState(false)

  // Restore previews for an in-progress draft (files already uploaded in an earlier session) -
  // fetched via apiClient (carries the auth header), not a plain <img src>/URL string, since
  // these files are now served through an authenticated endpoint.
  useEffect(() => {
    let cancelled = false
    uploadApi.fetchMyPhotoUrl().then((result) => {
      if (!cancelled && result) setPhotoPreviewUrl(result.url)
    })
    uploadApi.fetchMyPrcIdUrl().then((result) => {
      if (!cancelled && result) {
        setHasPrcId(true)
        URL.revokeObjectURL(result.url) // only needed the existence check, not the bytes
      }
    })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    return () => {
      if (photoPreviewUrl) URL.revokeObjectURL(photoPreviewUrl)
    }
  }, [photoPreviewUrl])

  const handlePhotoSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (!file) return
    setUploadError(null)
    if (file.size > MaxImageBytes) {
      setUploadError('That photo is too large (max 24 MB). Please choose a smaller file.')
      event.target.value = ''
      return
    }

    // Instant local preview - no need to wait for the round trip to see the picked photo.
    if (photoPreviewUrl) URL.revokeObjectURL(photoPreviewUrl)
    setPhotoPreviewUrl(URL.createObjectURL(file))

    setUploadingPhoto(true)
    try {
      await uploadApi.uploadMyPhoto(file)
    } catch (err) {
      setUploadError(describeUploadError(err, 'Could not upload photo. Make sure it is a JPG or PNG under 24 MB.'))
    } finally {
      setUploadingPhoto(false)
    }
  }

  const handlePrcIdSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (!file) return
    setUploadError(null)
    const isPdf = file.name.toLowerCase().endsWith('.pdf')
    const maxBytes = isPdf ? MaxPdfBytes : MaxImageBytes
    if (file.size > maxBytes) {
      setUploadError(
        isPdf ? 'That PDF is too large (max 2 MB). Please choose a smaller file.' : 'That file is too large (max 24 MB). Please choose a smaller file.',
      )
      event.target.value = ''
      return
    }

    setUploadingPrcId(true)
    try {
      await uploadApi.uploadMyPrcId(file)
      setHasPrcId(true)
    } catch (err) {
      setUploadError(describeUploadError(err, 'Could not upload PRC ID. Make sure it is a JPG, PNG, or PDF under the size limit.'))
    } finally {
      setUploadingPrcId(false)
    }
  }

  return (
    <div className="card max-w-3xl">
      <div className="card-header">
        <h6 className="card-title">Complete Your Membership Application</h6>
      </div>
      <div className="card-body">
        <StepIndicator step={step} maxStepReached={maxStepReached} onStepClick={onStepClick} navigating={navigating} />

        {error && <p className="text-sm text-danger mb-4">{error}</p>}

        <form
          onSubmit={(e) => {
            e.preventDefault()
            if (step < steps.length - 1) {
              onNext()
            } else {
              onSubmit(e)
            }
          }}
        >
          {step === 0 && (
            <div className="flex flex-col md:flex-row gap-6">
              <div className="flex flex-col items-center gap-2 shrink-0">
                <div className="size-24 rounded-full bg-default-150 flex items-center justify-center overflow-hidden">
                  {photoPreviewUrl ? (
                    <img src={photoPreviewUrl} alt="Profile" className="size-full object-cover" />
                  ) : (
                    <LuUserRound className="size-12 text-default-400" />
                  )}
                </div>
                <input ref={photoInputRef} type="file" accept=".jpg,.jpeg,.png" className="hidden" onChange={handlePhotoSelected} />
                <button
                  type="button"
                  onClick={() => photoInputRef.current?.click()}
                  disabled={uploadingPhoto}
                  className="btn btn-sm bg-primary text-white disabled:opacity-50"
                >
                  {uploadingPhoto ? 'Uploading…' : 'Upload Photo'}
                </button>
                <p className="text-xs text-default-500 text-center">JPG or PNG - photos are optimized automatically</p>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 flex-1">
                {uploadError && <p className="md:col-span-2 text-sm text-danger">{uploadError}</p>}
                <div>
                  <label className="block font-medium text-default-900 text-sm mb-2">Member Type</label>
                  <select className="form-input" required value={state.memberType} onChange={(e) => onChange('memberType', e.target.value)}>
                    <option value="">Select a member type…</option>
                    {Object.values(MemberTypes).map((t) => (
                      <option key={t} value={t}>
                        {t}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="block font-medium text-default-900 text-sm mb-2">Chapter</label>
                  <select className="form-input" required value={state.chapter} onChange={(e) => onChange('chapter', e.target.value)}>
                    <option value="">Select a chapter…</option>
                    {Object.values(Chapters).map((c) => (
                      <option key={c} value={c}>
                        {c}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="block font-medium text-default-900 text-sm mb-2">First Name</label>
                  <input className="form-input" required value={state.firstName} onChange={(e) => onChange('firstName', e.target.value)} />
                </div>
                <div>
                  <label className="block font-medium text-default-900 text-sm mb-2">Last Name</label>
                  <input className="form-input" required value={state.lastName} onChange={(e) => onChange('lastName', e.target.value)} />
                </div>
                <div>
                  <label className="block font-medium text-default-900 text-sm mb-2">Middle Name</label>
                  <input className="form-input" value={state.middleName} onChange={(e) => onChange('middleName', e.target.value)} />
                </div>
                <div>
                  <label className="block font-medium text-default-900 text-sm mb-2">Suffix</label>
                  <input className="form-input" value={state.suffix} onChange={(e) => onChange('suffix', e.target.value)} />
                </div>
                <div>
                  <label className="block font-medium text-default-900 text-sm mb-2">Date of Birth</label>
                  <input type="date" className="form-input" value={state.birthdate} onChange={(e) => onChange('birthdate', e.target.value)} />
                </div>
                <div>
                  <label className="block font-medium text-default-900 text-sm mb-2">Gender</label>
                  <div className="flex items-center gap-4 h-[42px]">
                    <label className="flex items-center gap-2 text-sm">
                      <input
                        type="radio"
                        name="gender"
                        className="form-radio"
                        checked={state.gender === 'Male'}
                        onChange={() => onChange('gender', 'Male')}
                      />
                      Male
                    </label>
                    <label className="flex items-center gap-2 text-sm">
                      <input
                        type="radio"
                        name="gender"
                        className="form-radio"
                        checked={state.gender === 'Female'}
                        onChange={() => onChange('gender', 'Female')}
                      />
                      Female
                    </label>
                  </div>
                </div>
                <div>
                  <label className="block font-medium text-default-900 text-sm mb-2">PRC License No.</label>
                  <input
                    className="form-input"
                    value={state.prcLicenseNo}
                    onChange={(e) => onChange('prcLicenseNo', e.target.value)}
                  />
                </div>
                <div className="md:col-span-2">
                  <label className="block font-medium text-default-900 text-sm mb-2">Upload PRC ID</label>
                  <div className="flex items-center gap-3">
                    <input
                      ref={prcIdInputRef}
                      type="file"
                      accept=".jpg,.jpeg,.png,.pdf"
                      className="hidden"
                      onChange={handlePrcIdSelected}
                    />
                    <button
                      type="button"
                      onClick={() => prcIdInputRef.current?.click()}
                      disabled={uploadingPrcId}
                      className="btn border border-default-200 disabled:opacity-50 inline-flex items-center gap-2"
                    >
                      <LuUpload className="size-4" />
                      {uploadingPrcId ? 'Uploading…' : hasPrcId ? 'Replace file' : 'Upload'}
                    </button>
                    <span className="text-xs text-default-500">
                      JPG or PNG photos are optimized automatically; PDF files must be under 2 MB.
                    </span>
                  </div>
                </div>
              </div>
            </div>
          )}

          {step === 1 && (
            <div className="grid grid-cols-1 gap-4">
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">Address</label>
                <input className="form-input" value={state.address} onChange={(e) => onChange('address', e.target.value)} />
              </div>
            </div>
          )}

          {step === 2 && (
            <div className="grid grid-cols-1 gap-4 text-sm">
              <p className="text-default-500">Your account was already created at sign-up - nothing to change here.</p>
              <div>
                <span className="text-default-500">Email</span>{' '}
                <span className="font-semibold text-default-800">{accountEmail}</span>
              </div>
              <div>
                <span className="text-default-500">Display Name</span>{' '}
                <span className="font-semibold text-default-800">{accountDisplayName}</span>
              </div>
            </div>
          )}

          {step === 3 && (
            <div className="flex flex-col gap-4">
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">Company</label>
                <input className="form-input" value={state.company} onChange={(e) => onChange('company', e.target.value)} />
              </div>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-x-8 gap-y-2 text-sm">
                <div>
                  <span className="text-default-500">Name</span>{' '}
                  <span className="font-semibold text-default-800">
                    {state.firstName} {state.middleName} {state.lastName} {state.suffix}
                  </span>
                </div>
                <div>
                  <span className="text-default-500">Member Type</span>{' '}
                  <span className="font-semibold text-default-800">{state.memberType}</span>
                </div>
                <div>
                  <span className="text-default-500">Chapter</span>{' '}
                  <span className="font-semibold text-default-800">{state.chapter}</span>
                </div>
                <div>
                  <span className="text-default-500">Address</span>{' '}
                  <span className="font-semibold text-default-800">{state.address || '-'}</span>
                </div>
              </div>
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="form-checkbox"
                  checked={state.agreedToTerms}
                  onChange={(e) => onChange('agreedToTerms', e.target.checked)}
                />
                I confirm the information above is accurate and agree to the membership terms and conditions.
              </label>
            </div>
          )}

          <div className="mt-8 flex items-center justify-between">
            <button
              type="button"
              onClick={onBack}
              disabled={step === 0 || navigating}
              className="btn border border-default-200 disabled:opacity-50"
            >
              Back
            </button>
            <button
              type="submit"
              disabled={submitting || navigating || (step === steps.length - 1 && !state.agreedToTerms)}
              className="btn bg-primary text-white disabled:opacity-50"
            >
              {step === steps.length - 1 ? (submitting ? 'Submitting…' : 'Submit Application') : 'Save & Continue'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
