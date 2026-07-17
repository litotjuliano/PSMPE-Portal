import { useState } from 'react'
import { LuSquarePen } from 'react-icons/lu'
import type { Member } from '../../../../core/types/member'
import { EmploymentStatuses } from '../../../../core/types/member'
import { memberApi } from '../../../../core/api/endpoints/memberApi'
import { StandardButton } from '../../components/shared/StandardButton'
import { buildFullProfileRequest, describeError } from './shared'

interface ProfessionalInformationSectionProps {
  member: Member
  onUpdated: (member: Member) => void
}

interface FormState {
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
    employmentStatus: member.employmentStatus ?? '',
    company: member.company ?? '',
    position: member.position ?? '',
    businessAddress: member.businessAddress ?? '',
    yearsOfPractice: member.yearsOfPractice !== null && member.yearsOfPractice !== undefined ? String(member.yearsOfPractice) : '',
    specialization: member.specialization ?? '',
    skills: member.skills ?? '',
  }
}

// Which of Company/Position/Business Address make sense to show for a given Employment Status -
// purely a display gate, since the whole section stays optional to save regardless of selection.
function showsCompanyAndPosition(employmentStatus: string): boolean {
  return employmentStatus === EmploymentStatuses.Employed
}

function showsBusinessAddress(employmentStatus: string): boolean {
  return employmentStatus === EmploymentStatuses.SelfEmployed || employmentStatus === EmploymentStatuses.BusinessOwner
}

export const ProfessionalInformationSection = ({ member, onUpdated }: ProfessionalInformationSectionProps) => {
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
    if (form.yearsOfPractice && Number(form.yearsOfPractice) < 0) {
      setError('Years of Practice cannot be negative.')
      return
    }

    setSaving(true)
    try {
      const updated = await memberApi.updateMyProfile(
        buildFullProfileRequest(member, {
          employmentStatus: form.employmentStatus || null,
          company: showsCompanyAndPosition(form.employmentStatus) || showsBusinessAddress(form.employmentStatus) ? form.company || null : null,
          position: showsCompanyAndPosition(form.employmentStatus) ? form.position || null : null,
          businessAddress: showsBusinessAddress(form.employmentStatus) ? form.businessAddress || null : null,
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

  const showCompanyAndPosition = showsCompanyAndPosition(form.employmentStatus)
  const showBusinessAddress = showsBusinessAddress(form.employmentStatus)

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h6 className="font-semibold text-default-800">Professional Information</h6>
        {!editing && (
          <StandardButton size="sm" icon={LuSquarePen} onClick={startEditing}>
            Edit
          </StandardButton>
        )}
      </div>

      {error && <p className="text-sm text-danger">{error}</p>}

      <p className="text-sm text-default-500">Optional - fill in as much as you'd like, whenever you're ready.</p>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-sm">
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Employment Status</span>
          {editing ? (
            <select
              className="form-input"
              value={form.employmentStatus}
              onChange={(e) => handleChange('employmentStatus', e.target.value)}
            >
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
