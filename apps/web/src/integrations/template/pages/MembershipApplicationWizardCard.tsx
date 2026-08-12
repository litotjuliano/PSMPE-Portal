import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { LuEye, LuUpload, LuUserRound } from 'react-icons/lu'
import { Chapters, CivilStatuses, EducationLevels, MemberTypes, SpecifiedProfessions } from '../../../core/types/member'
import {
  CHAPTER_YEAR_ERROR,
  CHAPTER_YEAR_MAX,
  CHAPTER_YEAR_MIN,
  deriveRmpValidUntil,
  formatPhLandline,
  formatPhMobile,
  isValidChapterYear,
  shouldDeriveValidUntil,
} from '../../../core/utils/memberFields'
import { uploadApi } from '../../../core/api/endpoints/uploadApi'
import { paymentApi, type MembershipFees } from '../../../core/api/endpoints/paymentApi'
import { describeError } from '../../../core/utils/apiError'
import { MAX_IMAGE_BYTES, MAX_PDF_BYTES, MAX_PROOF_OF_PAYMENT_BYTES } from '../../../core/constants/uploadLimits'
import { PipeStepper } from '../components/shared/PipeStepper'
import { FilePreviewModal } from '../components/shared/FilePreviewModal'
import { PhilippineAddressFields, type AddressValue } from '../components/shared/PhilippineAddressFields'

/** The address component speaks generic field names; mailing state keys are prefixed. */
const MAILING_FIELD = {
  houseNo: 'mailingHouseNo',
  street: 'mailingStreet',
  barangay: 'mailingBarangay',
  cityMunicipality: 'mailingCityMunicipality',
  province: 'mailingProvince',
  zipCode: 'mailingZipCode',
  country: 'mailingCountry',
} as const satisfies Record<keyof AddressValue, keyof MembershipApplicationState>

const peso = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })

// Mirrors MemberService's server-side checks - purely for fast client-side feedback, the server
// is still the source of truth (MemberService.UpsertMyProfileAsync/SubmitMyProfileAsync).
const PH_MOBILE_PATTERN = /^(\+63|63|0)9\d{9}$/

function isValidPhMobile(value: string): boolean {
  return PH_MOBILE_PATTERN.test(value)
}

/** Lenient - PH landline formats vary by area code length, so this only checks that what's left
 *  after stripping punctuation is a plausible 7-11 digit phone number (matches MemberService's
 *  IsValidHousePhone). */
function isValidHousePhone(value: string): boolean {
  if (!/^[\d\s\-()]+$/.test(value)) return false
  const digits = value.replace(/\D/g, '')
  return digits.length >= 7 && digits.length <= 11
}

function isAtLeast18(birthdate: string): boolean {
  const dob = new Date(birthdate)
  const eighteenYearsAgo = new Date()
  eighteenYearsAgo.setFullYear(eighteenYearsAgo.getFullYear() - 18)
  return dob <= eighteenYearsAgo
}

/** Display-only - the application form asks for Age but it's always derivable from Birthdate, so
 *  it's never sent to/stored by the backend. */
function computeAge(birthdate: string): number | null {
  if (!birthdate) return null
  const dob = new Date(birthdate)
  if (Number.isNaN(dob.getTime())) return null
  const today = new Date()
  let age = today.getFullYear() - dob.getFullYear()
  const hasHadBirthdayThisYear = today.getMonth() > dob.getMonth() || (today.getMonth() === dob.getMonth() && today.getDate() >= dob.getDate())
  if (!hasHadBirthdayThisYear) age -= 1
  return age >= 0 ? age : null
}

const maxBirthdate = (() => {
  const d = new Date()
  d.setFullYear(d.getFullYear() - 18)
  return d.toISOString().slice(0, 10)
})()

export interface MembershipApplicationState {
  firstName: string
  middleName: string
  lastName: string
  suffix: string
  birthdate: string
  gender: string
  civilStatus: string
  chapter: string
  /** Held as a string like every other input; converted to a number (or null) at payload time,
   *  same as yearsOfPractice elsewhere. */
  chapterYear: string
  chapterPosition: string
  memberType: string
  educationLevel: string
  schoolName: string
  courseYearGraduated: string
  specifiedProfession: string
  prcLicenseNo: string
  prcRegistrationDate: string
  prcValidUntilDate: string
  ptrNumber: string
  ptrPlaceIssued: string
  ptrDateIssued: string
  tin: string
  company: string
  mobileNumber: string
  houseNo: string
  street: string
  barangay: string
  cityMunicipality: string
  province: string
  zipCode: string
  country: string
  /** Client-only convenience - never sent as its own field; when true, the mailing address
   *  inputs are hidden and the residence values are copied into the mailing fields at save time
   *  (see MyProfilePage.saveDraft). */
  mailingSameAsResidence: boolean
  mailingHouseNo: string
  mailingStreet: string
  mailingBarangay: string
  mailingCityMunicipality: string
  mailingProvince: string
  mailingZipCode: string
  mailingCountry: string
  housePhone: string
  agreedToTerms: boolean
  dataPrivacyConsent: boolean
}

interface WizardFieldErrors {
  birthdate?: string
  chapterYear?: string
  photo?: string
  prcId?: string
  prcRegistrationDate?: string
  prcValidUntilDate?: string
  housePhone?: string
  mobileNumber?: string
  tin?: string
  proofOfPayment?: string
  terms?: string
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
  error: string | null
  submitting: boolean
  /** True while a stepper-click or Back save is in flight - disables navigation to prevent
   *  overlapping saves from rapid clicks. */
  navigating: boolean
}

const steps = ['Personal Information', 'Contact Information', 'Additional Information', 'Payment Details']

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
  error,
  submitting,
  navigating,
}: MembershipApplicationWizardCardProps) => {
  const photoInputRef = useRef<HTMLInputElement>(null)
  const prcIdInputRef = useRef<HTMLInputElement>(null)
  const proofOfPaymentInputRef = useRef<HTMLInputElement>(null)
  const [uploadingPhoto, setUploadingPhoto] = useState(false)
  const [uploadingPrcId, setUploadingPrcId] = useState(false)
  const [uploadingProofOfPayment, setUploadingProofOfPayment] = useState(false)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [photoPreviewUrl, setPhotoPreviewUrl] = useState<string | null>(null)
  const [hasPrcId, setHasPrcId] = useState(false)
  const [hasProofOfPayment, setHasProofOfPayment] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<WizardFieldErrors>({})
  const [previewOpen, setPreviewOpen] = useState<'prcId' | 'proofOfPayment' | null>(null)
  const [fees, setFees] = useState<MembershipFees | null>(null)

  useEffect(() => {
    // Falls back to showing an ellipsis rather than a wrong number if this fails.
    paymentApi.getFees().then(setFees).catch(() => {})
  }, [])

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
    uploadApi.fetchMyProofOfPaymentUrl().then((result) => {
      if (!cancelled && result) {
        setHasProofOfPayment(true)
        URL.revokeObjectURL(result.url)
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

  // Clears stale inline errors from whichever step was last validated when navigating away from
  // it (Back, or clicking a stepper circle) - otherwise they'd linger on a step that hasn't been
  // (re-)submitted yet.
  useEffect(() => {
    setFieldErrors({})
  }, [step])

  const handlePhotoSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (!file) return
    setUploadError(null)
    if (file.size > MAX_IMAGE_BYTES) {
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
      setUploadError(describeError(err, 'Could not upload photo. Make sure it is a JPG or PNG under 24 MB.'))
    } finally {
      setUploadingPhoto(false)
    }
  }

  const handlePrcIdSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (!file) return
    setUploadError(null)
    const isPdf = file.name.toLowerCase().endsWith('.pdf')
    const maxBytes = isPdf ? MAX_PDF_BYTES : MAX_IMAGE_BYTES
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
      setUploadError(describeError(err, 'Could not upload RMP ID. Make sure it is a JPG, PNG, or PDF under the size limit.'))
    } finally {
      setUploadingPrcId(false)
    }
  }

  const handleProofOfPaymentSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (!file) return
    setUploadError(null)
    if (file.size > MAX_PROOF_OF_PAYMENT_BYTES) {
      setUploadError('That file is too large (max 1 MB). Please choose a smaller file.')
      event.target.value = ''
      return
    }

    setUploadingProofOfPayment(true)
    try {
      await uploadApi.uploadMyProofOfPayment(file)
      setHasProofOfPayment(true)
    } catch (err) {
      setUploadError(describeError(err, 'Could not upload your proof of payment. Make sure it is a JPG, PNG, or PDF under 1 MB.'))
    } finally {
      setUploadingProofOfPayment(false)
    }
  }

  /** Fast client-side feedback for fields the member has actually filled in - the server
   *  independently re-validates everything, this only saves a round trip on an obvious mistake.
   *  Scoped to exactly the fields owned by the current step, per-step. Collects every applicable
   *  error at once (not just the first) so each field can show its own inline message. */
  const validateStep = (): WizardFieldErrors => {
    const errors: WizardFieldErrors = {}
    if (step === 0) {
      if (state.birthdate && !isAtLeast18(state.birthdate)) {
        errors.birthdate = 'You must be at least 18 years old.'
      }
      if (!photoPreviewUrl) {
        errors.photo = 'Please upload your photo.'
      }
      if (!hasPrcId) {
        errors.prcId = 'Please upload your RMP ID.'
      }
      if (!state.prcRegistrationDate) {
        errors.prcRegistrationDate = 'Please enter your RMP Registration Date.'
      }
      if (!state.prcValidUntilDate) {
        errors.prcValidUntilDate = 'Please enter your RMP Valid Until date.'
      }
      if (state.chapterYear && !isValidChapterYear(state.chapterYear)) {
        errors.chapterYear = CHAPTER_YEAR_ERROR
      }
    } else if (step === 1) {
      if (state.housePhone && !isValidHousePhone(state.housePhone)) {
        errors.housePhone = 'House phone must be a valid landline number.'
      }
      if (state.mobileNumber && !isValidPhMobile(state.mobileNumber)) {
        errors.mobileNumber = 'Mobile number must be in the format +639XXXXXXXXX, 639XXXXXXXXX, or 09XXXXXXXXX.'
      }
    } else if (step === 2) {
      if (state.tin && !/^[\d-]{9,12}$/.test(state.tin)) {
        errors.tin = 'TIN must be 9-12 digits, with dashes allowed as separators.'
      }
    } else if (step === steps.length - 1) {
      if (!hasProofOfPayment) {
        errors.proofOfPayment = 'Please upload your proof of payment.'
      }
      if (!state.agreedToTerms || !state.dataPrivacyConsent) {
        errors.terms = 'Please agree to the membership terms and the data privacy consent.'
      }
    }
    return errors
  }

  const age = computeAge(state.birthdate)

  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">Complete Your Membership Application</h6>
      </div>
      <div className="card-body">
        <PipeStepper steps={steps} step={step} maxStepReached={maxStepReached} onStepClick={onStepClick} navigating={navigating} />

        {error && <p className="text-sm text-danger mb-4">{error}</p>}

        <form
          onSubmit={(e) => {
            e.preventDefault()
            const errors = validateStep()
            setFieldErrors(errors)
            if (Object.keys(errors).length > 0) {
              return
            }
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
                <p className="text-xs text-default-500 text-center">This photo will be used for your ID print.</p>
                {fieldErrors.photo && <p className="text-xs text-danger text-center">{fieldErrors.photo}</p>}
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 flex-1">
                {uploadError && <p className="md:col-span-2 text-sm text-danger">{uploadError}</p>}
                {/* Membership + chapter form one row on desktop: 1-up on phones, 2x2 on tablets,
                    4 across from xl. Nested inside the step's own 2-column grid via col-span-2,
                    the same way the Surname/Given/Middle/Suffix row below does it. */}
                <div className="md:col-span-2 grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
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
                  {/* Chapter officer post - optional, and shown for every chapter, not just NCR. */}
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">Chapter Officer Year (optional)</label>
                    <input
                      className="form-input"
                      type="number"
                      min={CHAPTER_YEAR_MIN}
                      max={CHAPTER_YEAR_MAX}
                      placeholder="e.g. 2024"
                      value={state.chapterYear}
                      onChange={(e) => onChange('chapterYear', e.target.value)}
                    />
                    {fieldErrors.chapterYear && <p className="text-xs text-danger mt-1">{fieldErrors.chapterYear}</p>}
                  </div>
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">Chapter Position (optional)</label>
                    <input
                      className="form-input"
                      placeholder="e.g. Secretary"
                      value={state.chapterPosition}
                      onChange={(e) => onChange('chapterPosition', e.target.value)}
                    />
                  </div>
                </div>
                <div className="md:col-span-2 grid grid-cols-2 sm:grid-cols-4 gap-4">
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">Surname</label>
                    <input className="form-input" required value={state.lastName} onChange={(e) => onChange('lastName', e.target.value)} />
                  </div>
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">Given Name</label>
                    <input className="form-input" required value={state.firstName} onChange={(e) => onChange('firstName', e.target.value)} />
                  </div>
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">Middle Name</label>
                    <input className="form-input" value={state.middleName} onChange={(e) => onChange('middleName', e.target.value)} />
                  </div>
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">Suffix</label>
                    <input className="form-input" value={state.suffix} onChange={(e) => onChange('suffix', e.target.value)} />
                  </div>
                </div>
                <div className="md:col-span-2 grid grid-cols-2 sm:grid-cols-4 gap-4">
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">Date of Birth</label>
                    <input
                      type="date"
                      className="form-input"
                      required
                      max={maxBirthdate}
                      value={state.birthdate}
                      onChange={(e) => onChange('birthdate', e.target.value)}
                    />
                    {fieldErrors.birthdate && <p className="text-xs text-danger mt-1">{fieldErrors.birthdate}</p>}
                  </div>
                  <div>
                    <span className="block font-medium text-default-900 text-sm mb-2">Age</span>
                    <span className="text-sm font-semibold text-default-800 h-[42px] flex items-center">{age ?? '-'}</span>
                  </div>
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">Gender</label>
                    <div className="flex items-center gap-4 h-[42px]">
                      <label className="flex items-center gap-2 text-sm">
                        <input
                          type="radio"
                          name="gender"
                          className="form-radio"
                          required
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
                    <label className="block font-medium text-default-900 text-sm mb-2">Civil Status</label>
                    <select className="form-input" required value={state.civilStatus} onChange={(e) => onChange('civilStatus', e.target.value)}>
                      <option value="">Select civil status…</option>
                      {Object.values(CivilStatuses).map((c) => (
                        <option key={c} value={c}>
                          {c}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>

                <div className="md:col-span-2 border-t border-default-200 pt-4 grid grid-cols-2 sm:grid-cols-4 gap-4">
                  <div>
                    <span className="block font-medium text-default-900 text-sm mb-2">Educational Record</span>
                    <div className="flex flex-wrap items-center gap-4">
                      {Object.values(EducationLevels).map((level) => (
                        <label key={level} className="flex items-center gap-2 text-sm">
                          <input
                            type="radio"
                            name="educationLevel"
                            className="form-radio"
                            required
                            checked={state.educationLevel === level}
                            onChange={() => onChange('educationLevel', level)}
                          />
                          {level}
                        </label>
                      ))}
                    </div>
                  </div>
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">Name of School/Institution</label>
                    <input className="form-input" required value={state.schoolName} onChange={(e) => onChange('schoolName', e.target.value)} />
                  </div>
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">Course &amp; Year Graduated</label>
                    <input
                      className="form-input"
                      required
                      placeholder="e.g. BSCE 2023"
                      value={state.courseYearGraduated}
                      onChange={(e) => onChange('courseYearGraduated', e.target.value)}
                    />
                  </div>
                  <div>
                    <span className="block font-medium text-default-900 text-sm mb-2">Specified Profession</span>
                    <div className="flex flex-wrap items-center gap-4">
                      {Object.values(SpecifiedProfessions).map((profession) => (
                        <label key={profession} className="flex items-center gap-2 text-sm">
                          <input
                            type="radio"
                            name="specifiedProfession"
                            className="form-radio"
                            required
                            checked={state.specifiedProfession === profession}
                            onChange={() => onChange('specifiedProfession', profession)}
                          />
                          {profession}
                        </label>
                      ))}
                    </div>
                  </div>
                </div>

                <div className="md:col-span-2 border-t border-default-200 pt-4 grid grid-cols-1 sm:grid-cols-3 gap-4">
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">RMP License No.</label>
                    <input
                      className="form-input"
                      required
                      value={state.prcLicenseNo}
                      onChange={(e) => onChange('prcLicenseNo', e.target.value)}
                    />
                  </div>
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">RMP Registration Date</label>
                    <input
                      type="date"
                      className="form-input"
                      required
                      value={state.prcRegistrationDate}
                      onChange={(e) => {
                        // Valid Until follows the registration date by a year, unless the applicant
                        // has already typed their own - see shouldDeriveValidUntil.
                        if (shouldDeriveValidUntil(state.prcValidUntilDate, state.prcRegistrationDate)) {
                          onChange('prcValidUntilDate', deriveRmpValidUntil(e.target.value))
                        }
                        onChange('prcRegistrationDate', e.target.value)
                      }}
                    />
                    {fieldErrors.prcRegistrationDate && <p className="text-xs text-danger mt-1">{fieldErrors.prcRegistrationDate}</p>}
                  </div>
                  <div>
                    <label className="block font-medium text-default-900 text-sm mb-2">RMP Valid Until</label>
                    <input
                      type="date"
                      className="form-input"
                      required
                      value={state.prcValidUntilDate}
                      onChange={(e) => onChange('prcValidUntilDate', e.target.value)}
                    />
                    <p className="text-xs text-default-500 mt-1">Defaults to one year after the registration date. Change it if your card says otherwise.</p>
                    {fieldErrors.prcValidUntilDate && <p className="text-xs text-danger mt-1">{fieldErrors.prcValidUntilDate}</p>}
                  </div>
                </div>
                <div className="md:col-span-2">
                  <label className="block font-medium text-default-900 text-sm mb-2">Upload RMP ID</label>
                  <div className="flex items-center gap-3">
                    <input
                      ref={prcIdInputRef}
                      type="file"
                      accept=".jpg,.jpeg,.png,.pdf"
                      className="hidden"
                      onChange={handlePrcIdSelected}
                    />
                    {hasPrcId && (
                      <button
                        type="button"
                        onClick={() => setPreviewOpen('prcId')}
                        className="btn border border-default-200 inline-flex items-center gap-2"
                      >
                        <LuEye className="size-4" />
                        View
                      </button>
                    )}
                    <button
                      type="button"
                      onClick={() => prcIdInputRef.current?.click()}
                      disabled={uploadingPrcId}
                      className="btn border border-default-200 disabled:opacity-50 inline-flex items-center gap-2"
                    >
                      <LuUpload className="size-4" />
                      {uploadingPrcId ? 'Uploading…' : hasPrcId ? 'Update' : 'Upload'}
                    </button>
                    <span className="text-xs text-default-500">
                      JPG or PNG photos are optimized automatically; PDF files must be under 2 MB.
                    </span>
                  </div>
                  {fieldErrors.prcId && <p className="text-xs text-danger mt-1">{fieldErrors.prcId}</p>}
                </div>
              </div>
            </div>
          )}

          {step === 1 && (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {/* Read-only - the account email is set at sign-up and changed from Account &
                  Security, not here. It sits with the other ways to reach the member. */}
              <div className="md:col-span-2">
                <span className="block font-medium text-default-900 text-sm mb-2">Email</span>
                <span className="text-sm font-semibold text-default-800">{accountEmail}</span>
              </div>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">House Phone (optional)</label>
                <input
                  className="form-input"
                  inputMode="tel"
                  placeholder="e.g. (02) 8123 4567"
                  value={state.housePhone}
                  onChange={(e) => onChange('housePhone', formatPhLandline(e.target.value))}
                />
                {fieldErrors.housePhone && <p className="text-xs text-danger mt-1">{fieldErrors.housePhone}</p>}
              </div>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">Mobile Number</label>
                <input
                  className="form-input"
                  required
                  inputMode="tel"
                  placeholder="09XXXXXXXXX"
                  value={state.mobileNumber}
                  onChange={(e) => onChange('mobileNumber', formatPhMobile(e.target.value))}
                />
                {fieldErrors.mobileNumber && <p className="text-xs text-danger mt-1">{fieldErrors.mobileNumber}</p>}
              </div>

              <div className="md:col-span-2 border-t border-default-200 pt-4">
                <h6 className="font-semibold text-default-800">Residence Address</h6>
              </div>
              {/* `contents` so the component's fields become direct children of this step's own
                  grid rather than nesting a second one inside a cell. */}
              <PhilippineAddressFields
                idPrefix="wizard-residence"
                required
                gridClassName="contents"
                value={{
                  houseNo: state.houseNo,
                  street: state.street,
                  barangay: state.barangay,
                  cityMunicipality: state.cityMunicipality,
                  province: state.province,
                  zipCode: state.zipCode,
                  country: state.country,
                }}
                onChange={onChange}
              />

              <div className="md:col-span-2 border-t border-default-200 pt-4 flex items-center justify-between">
                <h6 className="font-semibold text-default-800">Mailing Address</h6>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="form-checkbox"
                    checked={state.mailingSameAsResidence}
                    onChange={(e) => onChange('mailingSameAsResidence', e.target.checked)}
                  />
                  Same as Residence Address
                </label>
              </div>
              {!state.mailingSameAsResidence && (
                <PhilippineAddressFields
                  idPrefix="wizard-mailing"
                  gridClassName="contents"
                  value={{
                    houseNo: state.mailingHouseNo,
                    street: state.mailingStreet,
                    barangay: state.mailingBarangay,
                    cityMunicipality: state.mailingCityMunicipality,
                    province: state.mailingProvince,
                    zipCode: state.mailingZipCode,
                    country: state.mailingCountry,
                  }}
                  onChange={(field, next) => onChange(MAILING_FIELD[field], next)}
                />
              )}
            </div>
          )}

          {step === 2 && (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">PTR Number (optional)</label>
                <input className="form-input" value={state.ptrNumber} onChange={(e) => onChange('ptrNumber', e.target.value)} />
              </div>
              {/* Nothing on this step is required - see MemberService.SubmitMyProfileAsync. */}
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">PTR Place Issued (optional)</label>
                <input
                  className="form-input"
                  placeholder="e.g. Quezon City"
                  value={state.ptrPlaceIssued}
                  onChange={(e) => onChange('ptrPlaceIssued', e.target.value)}
                />
              </div>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">PTR Date Issued (optional)</label>
                <input
                  className="form-input"
                  type="date"
                  value={state.ptrDateIssued}
                  onChange={(e) => onChange('ptrDateIssued', e.target.value)}
                />
              </div>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">TIN (optional)</label>
                <input
                  className="form-input"
                  placeholder="000-000-000-000"
                  value={state.tin}
                  onChange={(e) => onChange('tin', e.target.value)}
                />
                {fieldErrors.tin && <p className="text-xs text-danger mt-1">{fieldErrors.tin}</p>}
              </div>
              <div className="md:col-span-2">
                <label className="block font-medium text-default-900 text-sm mb-2">Company (optional)</label>
                <input className="form-input" value={state.company} onChange={(e) => onChange('company', e.target.value)} />
              </div>
            </div>
          )}

          {step === 3 && (
            <div className="flex flex-col gap-6">
              <div>
                <h6 className="font-semibold text-default-800 mb-3">Payment Details</h6>
                {/* Read from SystemConfig, not hardcoded - the same figures the receipt uses,
                    so the two can no longer drift apart. */}
                <div className="text-sm text-default-700 flex flex-col gap-1">
                  <p className="font-semibold text-default-800">TOTAL: {fees ? peso.format(fees.registrationTotal) : '…'}</p>
                  <p>Membership Fee: {fees ? peso.format(fees.membershipFee) : '…'}</p>
                  <p>Annual Dues: {fees ? peso.format(fees.annualDues) : '…'} (payable one year after registration)</p>
                  <p>PVC ID: Included</p>
                  <p>Shipping Fee (delivery option only): {fees ? peso.format(fees.shippingFee) : '…'}</p>
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mt-3 text-sm">
                  <div className="border border-default-200 rounded-lg p-3">
                    <p className="font-semibold text-default-800 mb-1">Bank Deposit</p>
                    <p>Account Name: PSMPE INC.</p>
                    <p>Account No: 0007-306506443</p>
                    <p>Metrobank</p>
                  </div>
                  <div className="border border-default-200 rounded-lg p-3">
                    <p className="font-semibold text-default-800 mb-1">GCash / Bank Transfer</p>
                    <p>Account Name: PSMPE INC.</p>
                    <p>Account No: 3067306506443</p>
                    <p>Metrobank</p>
                  </div>
                </div>
                <div className="mt-4">
                  <label className="block font-medium text-default-900 text-sm mb-2">Proof of Payment</label>
                  <div className="flex items-center gap-3">
                    <input
                      ref={proofOfPaymentInputRef}
                      type="file"
                      accept=".jpg,.jpeg,.png,.pdf"
                      className="hidden"
                      onChange={handleProofOfPaymentSelected}
                    />
                    {hasProofOfPayment && (
                      <button
                        type="button"
                        onClick={() => setPreviewOpen('proofOfPayment')}
                        className="btn border border-default-200 inline-flex items-center gap-2"
                      >
                        <LuEye className="size-4" />
                        View
                      </button>
                    )}
                    <button
                      type="button"
                      onClick={() => proofOfPaymentInputRef.current?.click()}
                      disabled={uploadingProofOfPayment}
                      className="btn border border-default-200 disabled:opacity-50 inline-flex items-center gap-2"
                    >
                      <LuUpload className="size-4" />
                      {uploadingProofOfPayment ? 'Uploading…' : hasProofOfPayment ? 'Update' : 'Upload'}
                    </button>
                    <span className="text-xs text-default-500">JPG, PNG, or PDF - must be under 1 MB.</span>
                  </div>
                  {fieldErrors.proofOfPayment && <p className="text-xs text-danger mt-1">{fieldErrors.proofOfPayment}</p>}
                </div>
              </div>

              <div className="border-t border-default-200 pt-4">
                <h6 className="font-semibold text-default-800 mb-3">Review your application</h6>
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
                    <span className="text-default-500">Birthdate</span>{' '}
                    <span className="font-semibold text-default-800">{state.birthdate || '-'}</span>
                  </div>
                  <div>
                    <span className="text-default-500">Mobile Number</span>{' '}
                    <span className="font-semibold text-default-800">{state.mobileNumber || '-'}</span>
                  </div>
                  <div className="md:col-span-2">
                    <span className="text-default-500">Residence Address</span>{' '}
                    <span className="font-semibold text-default-800">
                      {[state.houseNo, state.street, state.barangay, state.cityMunicipality, state.province, state.zipCode, state.country]
                        .filter(Boolean)
                        .join(', ') || '-'}
                    </span>
                  </div>
                  <div>
                    <span className="text-default-500">RMP License No.</span>{' '}
                    <span className="font-semibold text-default-800">{state.prcLicenseNo || '-'}</span>
                  </div>
                  <div>
                    <span className="text-default-500">PTR Number</span>{' '}
                    <span className="font-semibold text-default-800">{state.ptrNumber || '-'}</span>
                  </div>
                  <div>
                    <span className="text-default-500">Company</span>{' '}
                    <span className="font-semibold text-default-800">{state.company || '-'}</span>
                  </div>
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
              <label className="flex items-start gap-2 text-sm">
                <input
                  type="checkbox"
                  className="form-checkbox mt-0.5"
                  checked={state.dataPrivacyConsent}
                  onChange={(e) => onChange('dataPrivacyConsent', e.target.checked)}
                />
                <span>
                  I agree that my personal information may be collected, processed, stored, and maintained by the Association, in
                  digital, electronic, and/or printed form. My personal information shall be kept confidential and used solely for
                  legitimate organizational purposes in accordance with the Data Privacy Act of 2012 (Republic Act No. 10173).{' '}
                  <a
                    href="https://www.privacy.gov.ph/data-privacy-act/"
                    target="_blank"
                    rel="noreferrer"
                    className="text-primary underline"
                  >
                    Learn more
                  </a>
                  .
                </span>
              </label>
              {fieldErrors.terms && <p className="text-xs text-danger">{fieldErrors.terms}</p>}
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
              disabled={submitting || navigating || (step === steps.length - 1 && (!state.agreedToTerms || !state.dataPrivacyConsent))}
              className="btn bg-primary text-white disabled:opacity-50"
            >
              {step === steps.length - 1 ? (submitting ? 'Submitting…' : 'Submit Application') : 'Save & Continue'}
            </button>
          </div>
        </form>
      </div>

      <FilePreviewModal
        isOpen={previewOpen === 'prcId'}
        title="RMP ID"
        fetchFile={() => uploadApi.fetchMyPrcIdUrl()}
        onClose={() => setPreviewOpen(null)}
      />
      <FilePreviewModal
        isOpen={previewOpen === 'proofOfPayment'}
        title="Proof of Payment"
        fetchFile={() => uploadApi.fetchMyProofOfPaymentUrl()}
        onClose={() => setPreviewOpen(null)}
      />
    </div>
  )
}
