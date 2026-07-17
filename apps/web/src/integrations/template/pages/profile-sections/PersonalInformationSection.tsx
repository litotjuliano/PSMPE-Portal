import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import { LuSquarePen, LuUserRound } from 'react-icons/lu'
import type { Member } from '../../../../core/types/member'
import { CivilStatuses } from '../../../../core/types/member'
import { memberApi } from '../../../../core/api/endpoints/memberApi'
import { uploadApi } from '../../../../core/api/endpoints/uploadApi'
import { StandardButton } from '../../components/shared/StandardButton'
import { buildFullProfileRequest, describeError } from './shared'

// Matches the backend's MemberUploadService caps - see MembershipApplicationWizardCard.tsx.
const MaxImageBytes = 24 * 1024 * 1024

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
  }
}

export const PersonalInformationSection = ({ member, onUpdated }: PersonalInformationSectionProps) => {
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<FormState>(() => toFormState(member))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const photoInputRef = useRef<HTMLInputElement>(null)
  const [uploadingPhoto, setUploadingPhoto] = useState(false)
  const [photoPreviewUrl, setPhotoPreviewUrl] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    uploadApi.fetchMyPhotoUrl().then((result) => {
      if (!cancelled && result) setPhotoPreviewUrl(result.url)
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

  const handleSave = async () => {
    setError(null)
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
                <input className="form-input" value={form.gender} onChange={(e) => handleChange('gender', e.target.value)} />
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
            </>
          )}
        </div>
      </div>

      {editing && (
        <div className="flex items-center gap-2">
          <StandardButton onClick={handleSave} loading={saving} loadingLabel="Saving…">
            Save
          </StandardButton>
          <StandardButton variant="secondary" onClick={cancelEditing} disabled={saving}>
            Cancel
          </StandardButton>
        </div>
      )}
    </div>
  )
}
