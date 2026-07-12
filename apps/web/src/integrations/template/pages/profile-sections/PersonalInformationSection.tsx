import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import { LuEye, LuSquarePen, LuUpload, LuUserRound } from 'react-icons/lu'
import type { Member } from '../../../../core/types/member'
import { memberApi } from '../../../../core/api/endpoints/memberApi'
import { uploadApi } from '../../../../core/api/endpoints/uploadApi'
import { StandardButton } from '../../components/shared/StandardButton'
import { FilePreviewModal } from '../../components/shared/FilePreviewModal'
import { buildFullProfileRequest, describeError } from './shared'

// Matches the backend's MemberUploadService caps - see MembershipApplicationWizardCard.tsx.
const MaxImageBytes = 24 * 1024 * 1024
const MaxPdfBytes = 2 * 1024 * 1024

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
  prcLicenseNo: string
}

function toFormState(member: Member): FormState {
  return {
    firstName: member.firstName,
    middleName: member.middleName ?? '',
    lastName: member.lastName,
    suffix: member.suffix ?? '',
    birthdate: member.birthdate ?? '',
    gender: member.gender ?? '',
    prcLicenseNo: member.prcLicenseNo ?? '',
  }
}

export const PersonalInformationSection = ({ member, onUpdated }: PersonalInformationSectionProps) => {
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<FormState>(() => toFormState(member))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const photoInputRef = useRef<HTMLInputElement>(null)
  const prcIdInputRef = useRef<HTMLInputElement>(null)
  const [uploadingPhoto, setUploadingPhoto] = useState(false)
  const [uploadingPrcId, setUploadingPrcId] = useState(false)
  const [photoPreviewUrl, setPhotoPreviewUrl] = useState<string | null>(null)
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
    if (file.size > MaxImageBytes) {
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
    const maxBytes = isPdf ? MaxPdfBytes : MaxImageBytes
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
          prcLicenseNo: form.prcLicenseNo || null,
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
                <input className="form-input" value={form.gender} onChange={(e) => handleChange('gender', e.target.value)} />
              </div>
              <div className="md:col-span-2">
                <label className="block font-medium text-default-900 text-sm mb-2">PRC License No.</label>
                <input className="form-input" value={form.prcLicenseNo} onChange={(e) => handleChange('prcLicenseNo', e.target.value)} />
                {prcLicenseNoChanged && (
                  <p className="text-xs text-warning mt-1">
                    {prcIdJustReuploaded ? 'New PRC ID uploaded - ready to save.' : 'Upload a new PRC ID document below to save this change.'}
                  </p>
                )}
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
              <div className="md:col-span-2">
                <span className="block font-medium text-default-900 text-sm mb-2">PRC License No.</span>
                <span className="font-semibold text-default-800">{member.prcLicenseNo || '-'}</span>
                {member.pendingPrcLicenseNo ? (
                  <p className="text-xs text-warning mt-1">New value "{member.pendingPrcLicenseNo}" - pending admin verification.</p>
                ) : (
                  !member.prcIdVerified &&
                  member.prcLicenseNo && <p className="text-xs text-warning mt-1">Pending admin verification.</p>
                )}
              </div>
            </>
          )}

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
              {editing && (
                <>
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
                </>
              )}
            </div>
          </div>
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
