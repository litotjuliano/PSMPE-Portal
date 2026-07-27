import { useState } from 'react'
import { LuSquarePen, LuTriangleAlert } from 'react-icons/lu'
import type { Member } from '../../../../core/types/member'
import { memberApi } from '../../../../core/api/endpoints/memberApi'
import { StandardButton } from '../../components/shared/StandardButton'
import { buildFullProfileRequest, describeError } from './shared'

// Mirrors MemberService's server-side checks - purely for fast client-side feedback, the server
// is still the source of truth (MemberService.UpsertMyProfileAsync).
const PH_MOBILE_PATTERN = /^(\+63|63|0)9\d{9}$/

function isValidHousePhone(value: string): boolean {
  if (!/^[\d\s\-()]+$/.test(value)) return false
  const digits = value.replace(/\D/g, '')
  return digits.length >= 7 && digits.length <= 11
}

function isValidUrl(value: string): boolean {
  try {
    const url = new URL(value)
    return url.protocol === 'http:' || url.protocol === 'https:'
  } catch {
    return false
  }
}

interface ContactInformationSectionProps {
  member: Member
  onUpdated: (member: Member) => void
}

interface FormState {
  housePhone: string
  mobileNumber: string
  website: string
  facebookUrl: string
  linkedInUrl: string
  xUrl: string
  instagramUrl: string
  houseNo: string
  street: string
  barangay: string
  cityMunicipality: string
  province: string
  zipCode: string
  /** Client-only convenience - when true, the mailing inputs are hidden and residence values are
   *  copied into the mailing fields at save time. There's no stored flag; a returning edit infers
   *  false whenever the two addresses don't already match exactly. */
  mailingSameAsResidence: boolean
  mailingHouseNo: string
  mailingStreet: string
  mailingBarangay: string
  mailingCityMunicipality: string
  mailingProvince: string
  mailingZipCode: string
}

function toFormState(member: Member): FormState {
  const mailingSameAsResidence =
    (member.mailingHouseNo ?? '') === (member.houseNo ?? '') &&
    (member.mailingStreet ?? '') === (member.street ?? '') &&
    (member.mailingBarangay ?? '') === (member.barangay ?? '') &&
    (member.mailingCityMunicipality ?? '') === (member.cityMunicipality ?? '') &&
    (member.mailingProvince ?? '') === (member.province ?? '') &&
    (member.mailingZipCode ?? '') === (member.zipCode ?? '') &&
    Boolean(member.street || member.barangay || member.cityMunicipality || member.province || member.zipCode)

  return {
    housePhone: member.housePhone ?? '',
    mobileNumber: member.mobileNumber ?? '',
    website: member.website ?? '',
    facebookUrl: member.facebookUrl ?? '',
    linkedInUrl: member.linkedInUrl ?? '',
    xUrl: member.xUrl ?? '',
    instagramUrl: member.instagramUrl ?? '',
    houseNo: member.houseNo ?? '',
    street: member.street ?? '',
    barangay: member.barangay ?? '',
    cityMunicipality: member.cityMunicipality ?? '',
    province: member.province ?? '',
    zipCode: member.zipCode ?? '',
    mailingSameAsResidence,
    mailingHouseNo: member.mailingHouseNo ?? '',
    mailingStreet: member.mailingStreet ?? '',
    mailingBarangay: member.mailingBarangay ?? '',
    mailingCityMunicipality: member.mailingCityMunicipality ?? '',
    mailingProvince: member.mailingProvince ?? '',
    mailingZipCode: member.mailingZipCode ?? '',
  }
}

/** Fields required to submit a new application - existing members are only ever nudged to
 *  complete these, never blocked from saving unrelated changes (see MemberService.SubmitMyProfileAsync). */
function missingRequiredFields(member: Member): string[] {
  const missing: string[] = []
  if (!member.mobileNumber) missing.push('Mobile Number')
  if (!member.street || !member.barangay || !member.cityMunicipality || !member.province || !member.zipCode) {
    missing.push('Residence Address')
  }
  return missing
}

export const ContactInformationSection = ({ member, onUpdated }: ContactInformationSectionProps) => {
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
    if (form.housePhone && !isValidHousePhone(form.housePhone)) {
      setError('House phone must be a valid landline number.')
      return
    }
    if (form.mobileNumber && !PH_MOBILE_PATTERN.test(form.mobileNumber)) {
      setError('Mobile number must be in the format +639XXXXXXXXX, 639XXXXXXXXX, or 09XXXXXXXXX.')
      return
    }
    if (form.website && !isValidUrl(form.website)) {
      setError('Website must be a valid URL, e.g. https://example.com.')
      return
    }
    if (form.facebookUrl && !isValidUrl(form.facebookUrl)) {
      setError('Facebook must be a valid profile URL.')
      return
    }
    if (form.linkedInUrl && !isValidUrl(form.linkedInUrl)) {
      setError('LinkedIn must be a valid profile URL.')
      return
    }
    if (form.xUrl && !isValidUrl(form.xUrl)) {
      setError('X (Twitter) must be a valid profile URL.')
      return
    }
    if (form.instagramUrl && !isValidUrl(form.instagramUrl)) {
      setError('Instagram must be a valid profile URL.')
      return
    }

    const mailing = form.mailingSameAsResidence
      ? {
          mailingHouseNo: form.houseNo || null,
          mailingStreet: form.street || null,
          mailingBarangay: form.barangay || null,
          mailingCityMunicipality: form.cityMunicipality || null,
          mailingProvince: form.province || null,
          mailingZipCode: form.zipCode || null,
        }
      : {
          mailingHouseNo: form.mailingHouseNo || null,
          mailingStreet: form.mailingStreet || null,
          mailingBarangay: form.mailingBarangay || null,
          mailingCityMunicipality: form.mailingCityMunicipality || null,
          mailingProvince: form.mailingProvince || null,
          mailingZipCode: form.mailingZipCode || null,
        }

    setSaving(true)
    try {
      const updated = await memberApi.updateMyProfile(
        buildFullProfileRequest(member, {
          housePhone: form.housePhone || null,
          mobileNumber: form.mobileNumber || null,
          website: form.website || null,
          facebookUrl: form.facebookUrl || null,
          linkedInUrl: form.linkedInUrl || null,
          xUrl: form.xUrl || null,
          instagramUrl: form.instagramUrl || null,
          houseNo: form.houseNo || null,
          street: form.street || null,
          barangay: form.barangay || null,
          cityMunicipality: form.cityMunicipality || null,
          province: form.province || null,
          zipCode: form.zipCode || null,
          ...mailing,
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
        <h6 className="font-semibold text-default-800">Contact Information</h6>
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

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-sm">
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">House Phone</span>
          {editing ? (
            <input
              className="form-input"
              placeholder="e.g. (02) 8123 4567"
              value={form.housePhone}
              onChange={(e) => handleChange('housePhone', e.target.value)}
            />
          ) : (
            <span className="font-semibold text-default-800">{member.housePhone || '-'}</span>
          )}
        </div>
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Mobile Number</span>
          {editing ? (
            <input
              className="form-input"
              placeholder="09XXXXXXXXX"
              value={form.mobileNumber}
              onChange={(e) => handleChange('mobileNumber', e.target.value)}
            />
          ) : (
            <span className="font-semibold text-default-800">{member.mobileNumber || '-'}</span>
          )}
        </div>
        <div className="md:col-span-2">
          <span className="block font-medium text-default-900 text-sm mb-2">Email Address</span>
          <span className="font-semibold text-default-800">{member.email}</span>
        </div>
        <div className="md:col-span-2">
          <span className="block font-medium text-default-900 text-sm mb-2">Website</span>
          {editing ? (
            <input
              className="form-input"
              placeholder="https://example.com"
              value={form.website}
              onChange={(e) => handleChange('website', e.target.value)}
            />
          ) : (
            <span className="font-semibold text-default-800">{member.website || '-'}</span>
          )}
        </div>
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Facebook</span>
          {editing ? (
            <input
              className="form-input"
              placeholder="https://facebook.com/yourprofile"
              value={form.facebookUrl}
              onChange={(e) => handleChange('facebookUrl', e.target.value)}
            />
          ) : (
            <span className="font-semibold text-default-800">{member.facebookUrl || '-'}</span>
          )}
        </div>
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">LinkedIn</span>
          {editing ? (
            <input
              className="form-input"
              placeholder="https://linkedin.com/in/yourprofile"
              value={form.linkedInUrl}
              onChange={(e) => handleChange('linkedInUrl', e.target.value)}
            />
          ) : (
            <span className="font-semibold text-default-800">{member.linkedInUrl || '-'}</span>
          )}
        </div>
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">X</span>
          {editing ? (
            <input
              className="form-input"
              placeholder="https://x.com/yourprofile"
              value={form.xUrl}
              onChange={(e) => handleChange('xUrl', e.target.value)}
            />
          ) : (
            <span className="font-semibold text-default-800">{member.xUrl || '-'}</span>
          )}
        </div>
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Instagram</span>
          {editing ? (
            <input
              className="form-input"
              placeholder="https://instagram.com/yourprofile"
              value={form.instagramUrl}
              onChange={(e) => handleChange('instagramUrl', e.target.value)}
            />
          ) : (
            <span className="font-semibold text-default-800">{member.instagramUrl || '-'}</span>
          )}
        </div>
      </div>

      <div className="border-t border-default-200 pt-4">
        <h6 className="font-semibold text-default-800 mb-3">Residence Address</h6>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-sm">
          <div>
            <span className="block font-medium text-default-900 text-sm mb-2">House No.</span>
            {editing ? (
              <input className="form-input" value={form.houseNo} onChange={(e) => handleChange('houseNo', e.target.value)} />
            ) : (
              <span className="font-semibold text-default-800">{member.houseNo || '-'}</span>
            )}
          </div>
          <div>
            <span className="block font-medium text-default-900 text-sm mb-2">Street</span>
            {editing ? (
              <input className="form-input" value={form.street} onChange={(e) => handleChange('street', e.target.value)} />
            ) : (
              <span className="font-semibold text-default-800">{member.street || '-'}</span>
            )}
          </div>
          <div>
            <span className="block font-medium text-default-900 text-sm mb-2">Barangay</span>
            {editing ? (
              <input className="form-input" value={form.barangay} onChange={(e) => handleChange('barangay', e.target.value)} />
            ) : (
              <span className="font-semibold text-default-800">{member.barangay || '-'}</span>
            )}
          </div>
          <div>
            <span className="block font-medium text-default-900 text-sm mb-2">City or Municipality</span>
            {editing ? (
              <input
                className="form-input"
                value={form.cityMunicipality}
                onChange={(e) => handleChange('cityMunicipality', e.target.value)}
              />
            ) : (
              <span className="font-semibold text-default-800">{member.cityMunicipality || '-'}</span>
            )}
          </div>
          <div>
            <span className="block font-medium text-default-900 text-sm mb-2">Province</span>
            {editing ? (
              <input className="form-input" value={form.province} onChange={(e) => handleChange('province', e.target.value)} />
            ) : (
              <span className="font-semibold text-default-800">{member.province || '-'}</span>
            )}
          </div>
          <div>
            <span className="block font-medium text-default-900 text-sm mb-2">Zip Code</span>
            {editing ? (
              <input className="form-input" value={form.zipCode} onChange={(e) => handleChange('zipCode', e.target.value)} />
            ) : (
              <span className="font-semibold text-default-800">{member.zipCode || '-'}</span>
            )}
          </div>
        </div>
      </div>

      <div className="border-t border-default-200 pt-4">
        <div className="flex items-center justify-between mb-3">
          <h6 className="font-semibold text-default-800">Mailing Address</h6>
          {editing && (
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                className="form-checkbox"
                checked={form.mailingSameAsResidence}
                onChange={(e) => handleChange('mailingSameAsResidence', e.target.checked)}
              />
              Same as Residence Address
            </label>
          )}
        </div>
        {editing && !form.mailingSameAsResidence ? (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-sm">
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">House No.</span>
              <input
                className="form-input"
                value={form.mailingHouseNo}
                onChange={(e) => handleChange('mailingHouseNo', e.target.value)}
              />
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">Street</span>
              <input
                className="form-input"
                value={form.mailingStreet}
                onChange={(e) => handleChange('mailingStreet', e.target.value)}
              />
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">Barangay</span>
              <input
                className="form-input"
                value={form.mailingBarangay}
                onChange={(e) => handleChange('mailingBarangay', e.target.value)}
              />
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">City or Municipality</span>
              <input
                className="form-input"
                value={form.mailingCityMunicipality}
                onChange={(e) => handleChange('mailingCityMunicipality', e.target.value)}
              />
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">Province</span>
              <input
                className="form-input"
                value={form.mailingProvince}
                onChange={(e) => handleChange('mailingProvince', e.target.value)}
              />
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">Zip Code</span>
              <input
                className="form-input"
                value={form.mailingZipCode}
                onChange={(e) => handleChange('mailingZipCode', e.target.value)}
              />
            </div>
          </div>
        ) : !editing ? (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-sm">
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">House No.</span>
              <span className="font-semibold text-default-800">{member.mailingHouseNo || '-'}</span>
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">Street</span>
              <span className="font-semibold text-default-800">{member.mailingStreet || '-'}</span>
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">Barangay</span>
              <span className="font-semibold text-default-800">{member.mailingBarangay || '-'}</span>
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">City or Municipality</span>
              <span className="font-semibold text-default-800">{member.mailingCityMunicipality || '-'}</span>
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">Province</span>
              <span className="font-semibold text-default-800">{member.mailingProvince || '-'}</span>
            </div>
            <div>
              <span className="block font-medium text-default-900 text-sm mb-2">Zip Code</span>
              <span className="font-semibold text-default-800">{member.mailingZipCode || '-'}</span>
            </div>
          </div>
        ) : null}
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
