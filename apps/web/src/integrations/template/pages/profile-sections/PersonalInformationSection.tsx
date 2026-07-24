import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import { LuEye, LuSquarePen, LuTriangleAlert, LuUpload, LuUserRound } from 'react-icons/lu'
import type { Member } from '../../../../core/types/member'
import { CivilStatuses } from '../../../../core/types/member'
import { memberApi } from '../../../../core/api/endpoints/memberApi'
import { uploadApi } from '../../../../core/api/endpoints/uploadApi'
import { MAX_IMAGE_BYTES, MAX_PDF_BYTES } from '../../../../core/constants/uploadLimits'
import { StandardButton } from '../../components/shared/StandardButton'
import { FilePreviewModal } from '../../components/shared/FilePreviewModal'
import { buildFullProfileRequest, describeError } from './shared'

interface PersonalInformationSectionProps {
  member: Member
  onUpdated: (member: Member) => void
}

interface FormState {
  firstName: string
  middleName: string
  lastName: string
  suffix: string
  birthdate: string
  gender: string
  civilStatus: string
  prcLicenseNo: string
  ptrNumber: string
  tin: string
}

function toFormState(member: Member): FormState {
  return {
    firstName: member.firstName,
    middleName: member.middleName ?? '',
    lastName: member.lastName,
    suffix: member.suffix ?? '',
    birthdate: member.birthdate ?? '',
    gender: member.gender ?? '',
    civilStatus: member.civilStatus ?? '',
    prcLicenseNo: member.prcLicenseNo ?? '',
    ptrNumber: member.ptrNumber ?? '',
    tin: member.tin ?? '',
  }
}

/** Fields required to submit a new application - existing members are only ever nudged to
 *  complete these, never blocked from saving unrelated changes (see MemberService.SubmitMyProfileAsync). */
function missingRequiredFields(member: Member): string[] {
  const missing: string[] = []
  if (!member.prcLicenseNo) missing.push('PRC License No.')
  if (!member.ptrNumber) missing.push('PTR Number')
  return missing
}

export const PersonalInformationSection = ({ member, onUpdated }: PersonalInformationSectionProps) => {
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<FormState>(() => toFormState(member))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const photoInputRef = useRef<HTMLInputElement>(null)
  const [uploadingPhoto, setUploadingPhoto] = useState(false)
  const [photoPreviewUrl, setPhotoPreviewUrl] = useState<string | null>(null)

  const prcIdInputRef = useRef<HTMLInputElement>(null)
  const [uploadingPrcId, setUploadingPrcId] = useState(false)
  const [hasPrcId, setHasPrcId] = useState(false)
  const [prcIdPreviewOpen, setPrcIdPreviewOpen] = useState(false)
  // Tracks whether the PRC ID was re-uploaded during *this* Edit Mode session - reset whenever
  // Edit Mode is (re-)entered, since a change made in an earlier session doesn't count.
  const [prcIdJustReuploaded, setPrcIdJustReuploaded] = useState(false)

  useEffect(() => {
    let cancelled = false
    uploadApi.fetchMyPhotoUrl().then((result) => {
      if (!cancelled && result) setPhotoPreviewUrl(result.url)
    })
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

  useEffect(() => {
    return () => {
      if (photoPreviewUrl) URL.revokeObjectURL(photoPreviewUrl)
    }
  }, [photoPreviewUrl])

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

  const handlePhotoSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (!file) return
    setError(null)
    if (file.size > MAX_IMAGE_BYTES) {
      setError('That photo is too large (max 24 MB). Please choose a smaller file.')
      event.target.value = ''
      return
    }

    if (photoPreviewUrl) URL.revokeObjectURL(photoPreviewUrl)
    setPhotoPreviewUrl(URL.createObjectURL(file))

    setUploadingPhoto(true)
    try {
      await uploadApi.uploadMyPhoto(file)
    } catch (err) {
      setError(describeError(err, 'Could not upload photo. Make sure it is a JPG or PNG under 24 MB.'))
    } finally {
      setUploadingPhoto(false)
    }
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
      setError(describeError(err, 'Could not upload PRC ID. Make sure it is a JPG, PNG, or PDF under the size limit.'))
    } finally {
      setUploadingPrcId(false)
    }
  }

  const prcLicenseNoChanged = form.prcLicenseNo !== (member.prcLicenseNo ?? '')
  const blockedByMissingReupload = prcLicenseNoChanged && !prcIdJustReuploaded

  const handleSave = async () => {
    setError(null)
    if (blockedByMissingReupload) {
      setError('Upload a new PRC ID document to save this change to PRC License No.')
      return
    }
    if (form.tin && !/^[\d-]{9,12}$/.test(form.tin)) {
      setError('TIN must be 9-12 digits, with dashes allowed as separators.')
      return
    }

    setSaving(true)
    try {
      const updated = await memberApi.updateMyProfile(
        buildFullProfileRequest(member, {
          firstName: form.firstName,
          middleName: form.middleName || null,
          lastName: form.lastName,
          suffix: form.suffix || null,
          birthdate: form.birthdate || null,
          gender: form.gender || null,
          civilStatus: form.civilStatus || null,
          prcLicenseNo: form.prcLicenseNo || null,
          ptrNumber: form.ptrNumber || null,
          tin: form.tin || null,
          prcIdReuploaded: prcLicenseNoChanged && prcIdJustReuploaded,
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
        <h6 className="font-semibold text-default-800">Personal Information</h6>
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
          Your requested PRC License No. change was not approved: {member.prcVerificationRejectedReason}
        </p>
      )}

      <div className="flex flex-col md:flex-row gap-6">
        <div className="flex flex-col items-center gap-2 shrink-0">
          <div className="size-24 rounded-full bg-default-150 flex items-center justify-center overflow-hidden">
            {photoPreviewUrl ? (
              <img src={photoPreviewUrl} alt="Profile" className="size-full object-cover" />
            ) : (
              <LuUserRound className="size-12 text-default-400" />
            )}
          </div>
          {editing && (
            <>
              <input ref={photoInputRef} type="file" accept=".jpg,.jpeg,.png" className="hidden" onChange={handlePhotoSelected} />
              <StandardButton size="sm" onClick={() => photoInputRef.current?.click()} loading={uploadingPhoto} loadingLabel="Uploading…">
                Upload Photo
              </StandardButton>
            </>
          )}
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 flex-1 text-sm">
          <div>
            <span className="block font-medium text-default-900 text-sm mb-2">Email</span>
            <span className="font-semibold text-default-800">{member.email}</span>
          </div>
          <div>
            <span className="block font-medium text-default-900 text-sm mb-2">Member Type</span>
            <span className="font-semibold text-default-800">{member.memberType}</span>
          </div>
          <div>
            <span className="block font-medium text-default-900 text-sm mb-2">Chapter</span>
            <span className="font-semibold text-default-800">{member.chapter}</span>
          </div>

          {editing ? (
            <>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">First Name</label>
                <input className="form-input" value={form.firstName} onChange={(e) => handleChange('firstName', e.target.value)} />
              </div>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">Last Name</label>
                <input className="form-input" value={form.lastName} onChange={(e) => handleChange('lastName', e.target.value)} />
              </div>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">Middle Name</label>
                <input className="form-input" value={form.middleName} onChange={(e) => handleChange('middleName', e.target.value)} />
              </div>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">Suffix</label>
                <input className="form-input" value={form.suffix} onChange={(e) => handleChange('suffix', e.target.value)} />
              </div>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">Birthdate</label>
                <input type="date" className="form-input" value={form.birthdate} onChange={(e) => handleChange('birthdate', e.target.value)} />
              </div>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">Gender</label>
                <div className="flex items-center gap-4 h-[42px]">
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="radio"
                      name="gender"
                      className="form-radio"
                      checked={form.gender === 'Male'}
                      onChange={() => handleChange('gender', 'Male')}
                    />
                    Male
                  </label>
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="radio"
                      name="gender"
                      className="form-radio"
                      checked={form.gender === 'Female'}
                      onChange={() => handleChange('gender', 'Female')}
                    />
                    Female
                  </label>
                </div>
              </div>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">Civil Status</label>
                <select className="form-input" value={form.civilStatus} onChange={(e) => handleChange('civilStatus', e.target.value)}>
                  <option value="">Select civil status…</option>
                  {Object.values(CivilStatuses).map((c) => (
                    <option key={c} value={c}>
                      {c}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">PRC License No.</label>
                <input className="form-input" value={form.prcLicenseNo} onChange={(e) => handleChange('prcLicenseNo', e.target.value)} />
                {prcLicenseNoChanged && (
                  <p className="text-xs text-warning mt-1">
                    {prcIdJustReuploaded ? 'New PRC ID uploaded - ready to save.' : 'Upload a new PRC ID document below to save this change.'}
                  </p>
                )}
              </div>
              <div>
                <label className="block font-medium text-default-900 text-sm mb-2">PTR Number</label>
                <input className="form-input" value={form.ptrNumber} onChange={(e) => handleChange('ptrNumber', e.target.value)} />
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
              <div className="md:col-span-2">
                <span className="block font-medium text-default-900 text-sm mb-2">PRC ID Document</span>
                <div className="flex items-center gap-3">
                  {hasPrcId ? (
                    <StandardButton variant="view" icon={LuEye} onClick={() => setPrcIdPreviewOpen(true)}>
                      View PRC ID
                    </StandardButton>
                  ) : (
                    <span className="text-default-500">No PRC ID uploaded yet.</span>
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
                <span className="block font-medium text-default-900 text-sm mb-2">First Name</span>
                <span className="font-semibold text-default-800">{member.firstName}</span>
              </div>
              <div>
                <span className="block font-medium text-default-900 text-sm mb-2">Last Name</span>
                <span className="font-semibold text-default-800">{member.lastName}</span>
              </div>
              <div>
                <span className="block font-medium text-default-900 text-sm mb-2">Middle Name</span>
                <span className="font-semibold text-default-800">{member.middleName || '-'}</span>
              </div>
              <div>
                <span className="block font-medium text-default-900 text-sm mb-2">Suffix</span>
                <span className="font-semibold text-default-800">{member.suffix || '-'}</span>
              </div>
              <div>
                <span className="block font-medium text-default-900 text-sm mb-2">Birthdate</span>
                <span className="font-semibold text-default-800">{member.birthdate || '-'}</span>
              </div>
              <div>
                <span className="block font-medium text-default-900 text-sm mb-2">Gender</span>
                <span className="font-semibold text-default-800">{member.gender || '-'}</span>
              </div>
              <div>
                <span className="block font-medium text-default-900 text-sm mb-2">Civil Status</span>
                <span className="font-semibold text-default-800">{member.civilStatus || '-'}</span>
              </div>
              <div>
                <span className="block font-medium text-default-900 text-sm mb-2">PRC License No.</span>
                <span className="font-semibold text-default-800">{member.prcLicenseNo || '-'}</span>
                {member.pendingPrcLicenseNo ? (
                  <p className="text-xs text-warning mt-1">New value "{member.pendingPrcLicenseNo}" - pending admin verification.</p>
                ) : (
                  !member.prcIdVerified &&
                  member.prcLicenseNo && <p className="text-xs text-warning mt-1">Pending admin verification.</p>
                )}
              </div>
              <div>
                <span className="block font-medium text-default-900 text-sm mb-2">PTR Number</span>
                <span className="font-semibold text-default-800">{member.ptrNumber || '-'}</span>
              </div>
              <div>
                <span className="block font-medium text-default-900 text-sm mb-2">TIN</span>
                <span className="font-semibold text-default-800">{member.tin || '-'}</span>
              </div>
              <div className="md:col-span-2">
                <span className="block font-medium text-default-900 text-sm mb-2">PRC ID Document</span>
                {hasPrcId ? (
                  <StandardButton variant="view" icon={LuEye} onClick={() => setPrcIdPreviewOpen(true)}>
                    View PRC ID
                  </StandardButton>
                ) : (
                  <span className="text-default-500">No PRC ID uploaded yet.</span>
                )}
              </div>
            </>
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
