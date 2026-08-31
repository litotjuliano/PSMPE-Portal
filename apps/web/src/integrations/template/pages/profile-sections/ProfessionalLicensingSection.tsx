import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import { LuEye, LuSquarePen, LuTriangleAlert, LuUpload } from 'react-icons/lu'
import type { Member } from '../../../../core/types/member'
import { EducationLevels, EmploymentStatuses, SpecifiedProfessions } from '../../../../core/types/member'
import { memberApi } from '../../../../core/api/endpoints/memberApi'
import { uploadApi } from '../../../../core/api/endpoints/uploadApi'
import { MAX_IMAGE_BYTES, MAX_PDF_BYTES } from '../../../../core/constants/uploadLimits'
import { StandardButton } from '../../components/shared/StandardButton'
import { StatusBadge } from '../../components/shared/StatusBadge'
import { FilePreviewModal } from '../../components/shared/FilePreviewModal'
import { buildFullProfileRequest, describeError } from './shared'
import { deriveRmpValidUntil, shouldDeriveValidUntil } from '../../../../core/utils/memberFields'

interface ProfessionalLicensingSectionProps {
  member: Member
  onUpdated: (member: Member) => void
}

interface FormState {
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
  employmentStatus: string
  company: string
  position: string
  businessAddress: string
  yearsOfPractice: string
  specialization: string
  skills: string
}

function toFormState(member: Member): FormState {
  return {
    educationLevel: member.educationLevel ?? '',
    schoolName: member.schoolName ?? '',
    courseYearGraduated: member.courseYearGraduated ?? '',
    specifiedProfession: member.specifiedProfession ?? '',
    prcLicenseNo: member.prcLicenseNo ?? '',
    prcRegistrationDate: member.prcRegistrationDate ?? '',
    prcValidUntilDate: member.prcValidUntilDate ?? '',
    ptrNumber: member.ptrNumber ?? '',
    ptrPlaceIssued: member.ptrPlaceIssued ?? '',
    ptrDateIssued: member.ptrDateIssued ?? '',
    tin: member.tin ?? '',
    employmentStatus: member.employmentStatus ?? '',
    company: member.company ?? '',
    position: member.position ?? '',
    businessAddress: member.businessAddress ?? '',
    yearsOfPractice: member.yearsOfPractice !== null && member.yearsOfPractice !== undefined ? String(member.yearsOfPractice) : '',
    specialization: member.specialization ?? '',
    skills: member.skills ?? '',
  }
}

/** Fields required to submit a new application - existing members are only ever nudged to
 *  complete these, never blocked from saving unrelated changes (see MemberService.SubmitMyProfileAsync). */
function missingRequiredFields(member: Member): string[] {
  const missing: string[] = []
  if (!member.educationLevel) missing.push('Educational Record')
  if (!member.schoolName) missing.push('School Name')
  if (!member.courseYearGraduated) missing.push('Course & Year Graduated')
  if (!member.specifiedProfession) missing.push('Specified Profession')
  if (!member.prcLicenseNo) missing.push('RMP License No.')
  if (!member.prcRegistrationDate) missing.push('RMP Registration Date')
  if (!member.prcValidUntilDate) missing.push('RMP Valid Until Date')
  // PTR Number is deliberately absent - it's optional now, so nudging for it would be wrong.
  return missing
}

// Which of Company/Position/Business Address make sense to show for a given Employment Status -
// purely a display gate, since the whole group stays optional to save regardless of selection.
function showsCompanyAndPosition(employmentStatus: string): boolean {
  return employmentStatus === EmploymentStatuses.Employed
}

function showsBusinessAddress(employmentStatus: string): boolean {
  return employmentStatus === EmploymentStatuses.SelfEmployed || employmentStatus === EmploymentStatuses.BusinessOwner
}

/**
 * Education, RMP/PRC licensing, and employment background in one tab - these three used to be
 * split across "Personal Information" (education/license, mixed in with personal identity fields)
 * and "Additional Information" (employment, mixed in with documents). Grouped here because all
 * three answer the same question: how is this member qualified and where do they work.
 */
export const ProfessionalLicensingSection = ({ member, onUpdated }: ProfessionalLicensingSectionProps) => {
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<FormState>(() => toFormState(member))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const prcIdInputRef = useRef<HTMLInputElement>(null)
  const [uploadingPrcId, setUploadingPrcId] = useState(false)
  const [hasPrcId, setHasPrcId] = useState(false)
  const [prcIdPreviewOpen, setPrcIdPreviewOpen] = useState(false)
  // Tracks whether the PRC ID was re-uploaded during *this* Edit Mode session - reset whenever
  // Edit Mode is (re-)entered, since a change made in an earlier session doesn't count.
  const [prcIdJustReuploaded, setPrcIdJustReuploaded] = useState(false)

  useEffect(() => {
    let cancelled = false
    uploadApi.fetchMyPrcIdUrl().then((result) => {
      if (!cancelled && result) {
        setHasPrcId(true)
        URL.revokeObjectURL(result.url)
      }
    })
    return () => {
      cancelled = true
    }
  }, [])

  const startEditing = () => {
    setForm(toFormState(member))
    setPrcIdJustReuploaded(false)
    setError(null)
    setEditing(true)
  }

  const cancelEditing = () => {
    setForm(toFormState(member))
    setError(null)
    setEditing(false)
  }

  const handleChange = <K extends keyof FormState>(field: K, value: FormState[K]) => {
    setForm((current) => ({ ...current, [field]: value }))
  }

  const handlePrcIdSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (!file) return
    setError(null)
    const isPdf = file.name.toLowerCase().endsWith('.pdf')
    const maxBytes = isPdf ? MAX_PDF_BYTES : MAX_IMAGE_BYTES
    if (file.size > maxBytes) {
      setError(
        isPdf ? 'That PDF is too large (max 2 MB). Please choose a smaller file.' : 'That file is too large (max 24 MB). Please choose a smaller file.',
      )
      event.target.value = ''
      return
    }

    setUploadingPrcId(true)
    try {
      await uploadApi.uploadMyPrcId(file)
      setHasPrcId(true)
      setPrcIdJustReuploaded(true)
    } catch (err) {
      setError(describeError(err, 'Could not upload RMP ID. Make sure it is a JPG, PNG, or PDF under the size limit.'))
    } finally {
      setUploadingPrcId(false)
    }
  }

  const prcLicenseNoChanged = form.prcLicenseNo !== (member.prcLicenseNo ?? '')
  const prcRegistrationDateChanged = form.prcRegistrationDate !== (member.prcRegistrationDate ?? '')
  const prcValidUntilDateChanged = form.prcValidUntilDate !== (member.prcValidUntilDate ?? '')
  const prcCardChanged = prcLicenseNoChanged || prcRegistrationDateChanged || prcValidUntilDateChanged
  const blockedByMissingReupload = prcCardChanged && !prcIdJustReuploaded

  const showCompanyAndPosition = showsCompanyAndPosition(form.employmentStatus)
  const showBusinessAddress = showsBusinessAddress(form.employmentStatus)

  const handleSave = async () => {
    setError(null)
    if (blockedByMissingReupload) {
      setError('Upload a new RMP ID document to save this change to RMP License No./Registration Date/Valid Until Date.')
      return
    }
    if (form.tin && !/^[\d-]{9,12}$/.test(form.tin)) {
      setError('TIN must be 9-12 digits, with dashes allowed as separators.')
      return
    }
    if (form.yearsOfPractice && Number(form.yearsOfPractice) < 0) {
      setError('Years of Practice cannot be negative.')
      return
    }

    setSaving(true)
    try {
      const updated = await memberApi.updateMyProfile(
        buildFullProfileRequest(member, {
          educationLevel: form.educationLevel || null,
          schoolName: form.schoolName || null,
          courseYearGraduated: form.courseYearGraduated || null,
          specifiedProfession: form.specifiedProfession || null,
          prcLicenseNo: form.prcLicenseNo || null,
          prcRegistrationDate: form.prcRegistrationDate || null,
          prcValidUntilDate: form.prcValidUntilDate || null,
          ptrNumber: form.ptrNumber || null,
          ptrPlaceIssued: form.ptrPlaceIssued || null,
          ptrDateIssued: form.ptrDateIssued || null,
          tin: form.tin || null,
          prcIdReuploaded: prcCardChanged && prcIdJustReuploaded,
          employmentStatus: form.employmentStatus || null,
          company: showCompanyAndPosition || showBusinessAddress ? form.company || null : null,
          position: showCompanyAndPosition ? form.position || null : null,
          businessAddress: showBusinessAddress ? form.businessAddress || null : null,
          yearsOfPractice: form.yearsOfPractice !== '' ? Number(form.yearsOfPractice) : null,
          specialization: form.specialization || null,
          skills: form.skills || null,
        }),
      )
      onUpdated(updated)
      setEditing(false)
    } catch (err) {
      setError(describeError(err, 'Could not save your changes. Please try again.'))
    } finally {
      setSaving(false)
    }
  }

  const missing = missingRequiredFields(member)

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h6 className="font-semibold text-default-800">Professional &amp; Licensing</h6>
        {!editing && (
          <StandardButton size="sm" icon={LuSquarePen} onClick={startEditing}>
            Edit
          </StandardButton>
        )}
      </div>

      {error && <p className="text-sm text-danger">{error}</p>}

      {!editing && missing.length > 0 && (
        <p className="text-sm text-warning bg-warning/10 rounded-lg px-3 py-2 flex items-start gap-2">
          <LuTriangleAlert className="size-4 shrink-0 mt-0.5" />
          Please complete the following: {missing.join(', ')}.
        </p>
      )}

      {member.prcVerificationRejectedReason && (
        <p className="text-sm text-danger bg-danger/10 rounded-lg px-3 py-2">
          Your requested RMP License change was not approved: {member.prcVerificationRejectedReason}
        </p>
      )}

      {/* Education */}
      <span className="text-xs font-semibold uppercase tracking-wide text-teal">Education</span>
      <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-3 gap-4 text-sm">
        {editing ? (
          <>
            <div className="md:col-span-2">
              <span className="block font-medium text-default-900 text-sm mb-2">Educational Record</span>
              <div className="flex items-center gap-4">
                {Object.values(EducationLevels).map((level) => (
                  <label key={level} className="flex items-center gap-2 text-sm">
                    <input
                      type="radio"
                      name="educationLevel"
                      className="form-radio"
                      checked={form.educationLevel === level}
                      onChange={() => handleChange('educationLevel', level)}
                    />
                    {level}
                  </label>
                ))}
              </div>
            </div>
            <div>
              <label className="block font-medium text-default-900 text-sm mb-2">Name of School/Institution</label>
              <input className="form-input" value={form.schoolName} onChange={(e) => handleChange('schoolName', e.target.value)} />
            </div>
            <div>
              <label className="block font-medium text-default-900 text-sm mb-2">Course &amp; Year Graduated</label>
              <input
                className="form-input"
                placeholder="e.g. BSCE 2023"
                value={form.courseYearGraduated}
                onChange={(e) => handleChange('courseYearGraduated', e.target.value)}
              />
            </div>
            <div className="md:col-span-2">
              <span className="block font-medium text-default-900 text-sm mb-2">Specified Profession</span>
              <div className="flex items-center gap-4">
                {Object.values(SpecifiedProfessions).map((profession) => (
                  <label key={profession} className="flex items-center gap-2 text-sm">
                    <input
                      type="radio"
                      name="specifiedProfession"
                      className="form-radio"
                      checked={form.specifiedProfession === profession}
                      onChange={() => handleChange('specifiedProfession', profession)}
                    />
                    {profession}
                  </label>
                ))}
              </div>
            </div>
          </>
        ) : (
          <>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">Educational Record</span>
              <span className="font-semibold text-default-800">{member.educationLevel || '-'}</span>
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">Name of School/Institution</span>
              <span className="font-semibold text-default-800">{member.schoolName || '-'}</span>
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">Course &amp; Year Graduated</span>
              <span className="font-semibold text-default-800">{member.courseYearGraduated || '-'}</span>
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">Specified Profession</span>
              <span className="font-semibold text-default-800">{member.specifiedProfession || '-'}</span>
            </div>
          </>
        )}
      </div>

      {/* RMP / PRC License */}
      <span className="text-xs font-semibold uppercase tracking-wide text-teal mt-2">RMP / PRC License</span>
      <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-3 gap-4 text-sm">
        {editing ? (
          <>
            <div>
              <label className="block font-medium text-default-900 text-sm mb-2">RMP License No.</label>
              <input className="form-input" value={form.prcLicenseNo} onChange={(e) => handleChange('prcLicenseNo', e.target.value)} />
            </div>
            <div>
              <label className="block font-medium text-default-900 text-sm mb-2">RMP Registration Date</label>
              <input
                type="date"
                className="form-input"
                value={form.prcRegistrationDate}
                onChange={(e) => {
                  if (shouldDeriveValidUntil(form.prcValidUntilDate, form.prcRegistrationDate)) {
                    handleChange('prcValidUntilDate', deriveRmpValidUntil(e.target.value))
                  }
                  handleChange('prcRegistrationDate', e.target.value)
                }}
              />
            </div>
            <div>
              <label className="block font-medium text-default-900 text-sm mb-2">RMP Valid Until</label>
              <input
                type="date"
                className="form-input"
                value={form.prcValidUntilDate}
                onChange={(e) => handleChange('prcValidUntilDate', e.target.value)}
              />
            </div>
            {prcCardChanged && (
              <div className="md:col-span-2 2xl:col-span-3 -mt-2">
                <p className="text-xs text-warning">
                  {prcIdJustReuploaded ? 'New RMP ID uploaded - ready to save.' : 'Upload a new RMP ID document below to save this change.'}
                </p>
              </div>
            )}
            <div>
              <label className="block font-medium text-default-900 text-sm mb-2">PTR Number</label>
              <input className="form-input" value={form.ptrNumber} onChange={(e) => handleChange('ptrNumber', e.target.value)} />
            </div>
            <div>
              <label className="block font-medium text-default-900 text-sm mb-2">PTR Place Issued</label>
              <input
                className="form-input"
                placeholder="e.g. Quezon City"
                value={form.ptrPlaceIssued}
                onChange={(e) => handleChange('ptrPlaceIssued', e.target.value)}
              />
            </div>
            <div>
              <label className="block font-medium text-default-900 text-sm mb-2">PTR Date Issued</label>
              <input
                className="form-input"
                type="date"
                value={form.ptrDateIssued}
                onChange={(e) => handleChange('ptrDateIssued', e.target.value)}
              />
            </div>
            <div>
              <label className="block font-medium text-default-900 text-sm mb-2">TIN</label>
              <input
                className="form-input"
                placeholder="000-000-000-000"
                value={form.tin}
                onChange={(e) => handleChange('tin', e.target.value)}
              />
            </div>
            <div className="md:col-span-2 2xl:col-span-3">
              <span className="block font-medium text-default-900 text-sm mb-2">RMP ID Document</span>
              <div className="flex items-center gap-3">
                {hasPrcId ? (
                  <StandardButton variant="view" icon={LuEye} onClick={() => setPrcIdPreviewOpen(true)}>
                    View RMP ID
                  </StandardButton>
                ) : (
                  <span className="text-default-500">No RMP ID uploaded yet.</span>
                )}
                <input ref={prcIdInputRef} type="file" accept=".jpg,.jpeg,.png,.pdf" className="hidden" onChange={handlePrcIdSelected} />
                <StandardButton
                  variant="secondary"
                  icon={LuUpload}
                  onClick={() => prcIdInputRef.current?.click()}
                  loading={uploadingPrcId}
                  loadingLabel="Uploading…"
                >
                  {hasPrcId ? 'Replace file' : 'Upload'}
                </StandardButton>
              </div>
            </div>
          </>
        ) : (
          <>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">RMP License No.</span>
              <span className="font-semibold text-default-800">{member.prcLicenseNo || '-'}</span>
              {member.pendingPrcLicenseNo ? (
                <p className="text-xs text-warning mt-1">New value "{member.pendingPrcLicenseNo}" - pending admin verification.</p>
              ) : (
                !member.prcIdVerified &&
                member.prcLicenseNo && <p className="text-xs text-warning mt-1">Pending admin verification.</p>
              )}
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">RMP Registration Date</span>
              <span className="font-semibold text-default-800">{member.prcRegistrationDate || '-'}</span>
              {member.pendingPrcRegistrationDate && (
                <p className="text-xs text-warning mt-1">New value "{member.pendingPrcRegistrationDate}" - pending admin verification.</p>
              )}
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">RMP Valid Until</span>
              <span className="font-semibold text-default-800 inline-flex items-center gap-2 flex-wrap">
                {member.prcValidUntilDate || '-'}
                {/* Nothing server-side computes licence expiry - derived here, and this is the
                    only place in the product that warns about it. */}
                {member.prcValidUntilDate && (
                  <StatusBadge variant={new Date(member.prcValidUntilDate).getTime() < Date.now() ? 'rejected' : 'active'}>
                    {new Date(member.prcValidUntilDate).getTime() < Date.now() ? 'Expired' : 'Valid'}
                  </StatusBadge>
                )}
              </span>
              {member.pendingPrcValidUntilDate && (
                <p className="text-xs text-warning mt-1">New value "{member.pendingPrcValidUntilDate}" - pending admin verification.</p>
              )}
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">PTR Number</span>
              <span className="font-semibold text-default-800">{member.ptrNumber || '-'}</span>
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">PTR Place Issued</span>
              <span className="font-semibold text-default-800">{member.ptrPlaceIssued || '-'}</span>
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">PTR Date Issued</span>
              <span className="font-semibold text-default-800">{member.ptrDateIssued || '-'}</span>
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">TIN</span>
              <span className="font-semibold text-default-800">{member.tin || '-'}</span>
            </div>
            <div className="md:col-span-2 2xl:col-span-3">
              <span className="block font-medium text-default-900 text-sm mb-2">RMP ID Document</span>
              {hasPrcId ? (
                <StandardButton variant="view" icon={LuEye} onClick={() => setPrcIdPreviewOpen(true)}>
                  View RMP ID
                </StandardButton>
              ) : (
                <span className="text-default-500">No RMP ID uploaded yet.</span>
              )}
            </div>
          </>
        )}
      </div>

      {/* Employment */}
      <span className="text-xs font-semibold uppercase tracking-wide text-teal mt-2">Employment</span>
      <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-3 gap-4 text-sm">
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Employment Status</span>
          {editing ? (
            <select className="form-input" value={form.employmentStatus} onChange={(e) => handleChange('employmentStatus', e.target.value)}>
              <option value="">Select employment status…</option>
              {Object.values(EmploymentStatuses).map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          ) : (
            <span className="font-semibold text-default-800">{member.employmentStatus || '-'}</span>
          )}
        </div>

        {(editing ? showCompanyAndPosition || showBusinessAddress : member.company) && (
          <div>
            <span className="block font-medium text-default-900 text-sm mb-2">Company</span>
            {editing ? (
              <input className="form-input" value={form.company} onChange={(e) => handleChange('company', e.target.value)} />
            ) : (
              <span className="font-semibold text-default-800">{member.company || '-'}</span>
            )}
          </div>
        )}

        {(editing ? showCompanyAndPosition : member.position) && (
          <div>
            <span className="block font-medium text-default-900 text-sm mb-2">Position</span>
            {editing ? (
              <input className="form-input" value={form.position} onChange={(e) => handleChange('position', e.target.value)} />
            ) : (
              <span className="font-semibold text-default-800">{member.position || '-'}</span>
            )}
          </div>
        )}

        {(editing ? showBusinessAddress : member.businessAddress) && (
          <div className="md:col-span-2">
            <span className="block font-medium text-default-900 text-sm mb-2">Business Address</span>
            {editing ? (
              <input className="form-input" value={form.businessAddress} onChange={(e) => handleChange('businessAddress', e.target.value)} />
            ) : (
              <span className="font-semibold text-default-800">{member.businessAddress || '-'}</span>
            )}
          </div>
        )}

        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Years of Practice</span>
          {editing ? (
            <input
              type="number"
              min={0}
              className="form-input"
              value={form.yearsOfPractice}
              onChange={(e) => handleChange('yearsOfPractice', e.target.value)}
            />
          ) : (
            <span className="font-semibold text-default-800">{member.yearsOfPractice ?? '-'}</span>
          )}
        </div>
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Specialization</span>
          {editing ? (
            <input className="form-input" value={form.specialization} onChange={(e) => handleChange('specialization', e.target.value)} />
          ) : (
            <span className="font-semibold text-default-800">{member.specialization || '-'}</span>
          )}
        </div>
        <div className="md:col-span-2">
          <span className="block font-medium text-default-900 text-sm mb-2">Skills</span>
          {editing ? (
            <input className="form-input" value={form.skills} onChange={(e) => handleChange('skills', e.target.value)} />
          ) : (
            <span className="font-semibold text-default-800">{member.skills || '-'}</span>
          )}
        </div>
      </div>

      {editing && (
        <div className="flex items-center gap-2">
          <StandardButton onClick={handleSave} disabled={blockedByMissingReupload} loading={saving} loadingLabel="Saving…">
            Save
          </StandardButton>
          <StandardButton variant="secondary" onClick={cancelEditing} disabled={saving}>
            Cancel
          </StandardButton>
        </div>
      )}

      <FilePreviewModal
        isOpen={prcIdPreviewOpen}
        title="PRC ID Document"
        fetchFile={() => uploadApi.fetchMyPrcIdUrl()}
        onClose={() => setPrcIdPreviewOpen(false)}
      />
    </div>
  )
}
