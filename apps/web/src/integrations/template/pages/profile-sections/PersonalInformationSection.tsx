import { useState } from 'react'
import { LuSquarePen } from 'react-icons/lu'
import type { Member } from '../../../../core/types/member'
import { CivilStatuses } from '../../../../core/types/member'
import { CHAPTER_YEAR_ERROR, CHAPTER_YEAR_MAX, CHAPTER_YEAR_MIN, isValidChapterYear } from '../../../../core/utils/memberFields'
import { memberApi } from '../../../../core/api/endpoints/memberApi'
import { StandardButton } from '../../components/shared/StandardButton'
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
  /** Held as a string like every other input; converted back to a number at save time. */
  chapterYear: string
  chapterPosition: string
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
    chapterYear: member.chapterYear !== null ? String(member.chapterYear) : '',
    chapterPosition: member.chapterPosition ?? '',
  }
}

/**
 * Pure personal identity - Education, RMP/PRC licensing, and Employment moved to Professional &
 * Licensing; Membership Type, Chapter, and Date Joined moved here from the identity rail
 * (ProfileRail), which narrowed to just photo + Membership ID so it stays usable at every width.
 * None of the three are self-service editable, hence the plain read-only group rather than form
 * inputs. The Chapter Officer pair below them is the exception - a post the member holds rather
 * than a term of their membership, so it stays editable here.
 */
export const PersonalInformationSection = ({ member, onUpdated }: PersonalInformationSectionProps) => {
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<FormState>(() => toFormState(member))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

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

  const handleSave = async () => {
    setError(null)
    if (!isValidChapterYear(form.chapterYear)) {
      setError(CHAPTER_YEAR_ERROR)
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
          chapterYear: form.chapterYear !== '' ? Number(form.chapterYear) : null,
          chapterPosition: form.chapterPosition || null,
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
        <h6 className="font-semibold text-default-800">Personal</h6>
        {!editing && (
          <StandardButton size="sm" icon={LuSquarePen} onClick={startEditing}>
            Edit
          </StandardButton>
        )}
      </div>

      {error && <p className="text-sm text-danger">{error}</p>}

      {/* Membership group - not editable here, matches the styling the identity rail used to
          carry for these three fields. */}
      <span className="text-xs font-semibold uppercase tracking-wide text-teal">Membership</span>
      <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-3 gap-4 text-sm">
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Membership Type</span>
          <span className="inline-flex items-center rounded bg-copper px-2.5 py-1 text-xs font-semibold uppercase tracking-wide text-white">
            {member.memberType}
          </span>
        </div>
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Chapter</span>
          <span className="font-semibold text-default-800">{member.chapter}</span>
        </div>
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Date Joined</span>
          <span className="font-semibold text-default-800">
            {member.approvedAt ? new Date(member.approvedAt).toLocaleDateString() : '-'}
          </span>
        </div>
      </div>

      {/* Unlike the three above, an officer post is self-service editable - it describes a role the
          member holds, not their eligibility, so it isn't locked post-submission. */}
      <span className="text-xs font-semibold uppercase tracking-wide text-teal mt-2">Chapter Officer</span>
      <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-3 gap-4 text-sm">
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Year</span>
          {editing ? (
            <input
              className="form-input"
              type="number"
              min={CHAPTER_YEAR_MIN}
              max={CHAPTER_YEAR_MAX}
              placeholder="e.g. 2024"
              value={form.chapterYear}
              onChange={(e) => handleChange('chapterYear', e.target.value)}
            />
          ) : (
            <span className="font-semibold text-default-800">{member.chapterYear ?? '-'}</span>
          )}
        </div>
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Position</span>
          {editing ? (
            <input
              className="form-input"
              placeholder="e.g. Secretary"
              value={form.chapterPosition}
              onChange={(e) => handleChange('chapterPosition', e.target.value)}
            />
          ) : (
            <span className="font-semibold text-default-800">{member.chapterPosition || '-'}</span>
          )}
        </div>
      </div>

      <span className="text-xs font-semibold uppercase tracking-wide text-teal mt-2">Personal Details</span>
      <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-3 gap-4 text-sm">
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
