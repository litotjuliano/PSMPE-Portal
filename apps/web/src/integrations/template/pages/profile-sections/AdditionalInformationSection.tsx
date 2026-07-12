import { useState } from 'react'
import { LuSquarePen } from 'react-icons/lu'
import type { Member } from '../../../../core/types/member'
import { memberApi } from '../../../../core/api/endpoints/memberApi'
import { StandardButton } from '../../components/shared/StandardButton'
import { buildFullProfileRequest, describeError } from './shared'

interface AdditionalInformationSectionProps {
  member: Member
  onUpdated: (member: Member) => void
}

export const AdditionalInformationSection = ({ member, onUpdated }: AdditionalInformationSectionProps) => {
  const [editing, setEditing] = useState(false)
  const [company, setCompany] = useState(member.company ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const startEditing = () => {
    setCompany(member.company ?? '')
    setError(null)
    setEditing(true)
  }

  const cancelEditing = () => {
    setCompany(member.company ?? '')
    setError(null)
    setEditing(false)
  }

  const handleSave = async () => {
    setError(null)
    setSaving(true)
    try {
      const updated = await memberApi.updateMyProfile(buildFullProfileRequest(member, { company: company || null }))
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
        <h6 className="font-semibold text-default-800">Additional Information</h6>
        {!editing && (
          <StandardButton size="sm" icon={LuSquarePen} onClick={startEditing}>
            Edit
          </StandardButton>
        )}
      </div>

      {error && <p className="text-sm text-danger">{error}</p>}

      <div>
        <span className="block font-medium text-default-900 text-sm mb-2">Company</span>
        {editing ? (
          <input className="form-input" value={company} onChange={(e) => setCompany(e.target.value)} />
        ) : (
          <span className="text-sm font-semibold text-default-800">{member.company || '-'}</span>
        )}
      </div>

      {editing && (
        <div className="flex items-center gap-2">
          <StandardButton onClick={handleSave} disabled={saving} loading={saving} loadingLabel="Saving…">
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
