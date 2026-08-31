import { useState } from 'react'
import { LuSquarePen, LuTriangleAlert } from 'react-icons/lu'
import type { Member } from '../../../../core/types/member'
import { memberApi } from '../../../../core/api/endpoints/memberApi'
import { StandardButton } from '../../components/shared/StandardButton'
import { PhilippineAddressFields, type AddressValue } from '../../components/shared/PhilippineAddressFields'
import { formatPhLandline, formatPhMobile, mailingMirrorsResidence } from '../../../../core/utils/memberFields'
import { buildFullProfileRequest, describeError } from './shared'

/** The address component speaks generic field names; mailing state keys are prefixed. `as const`
 *  keeps the literal key types so handleChange still type-checks its value against the right field. */
const MAILING_FIELD = {
  houseNo: 'mailingHouseNo',
  street: 'mailingStreet',
  barangay: 'mailingBarangay',
  cityMunicipality: 'mailingCityMunicipality',
  province: 'mailingProvince',
  zipCode: 'mailingZipCode',
  country: 'mailingCountry',
} as const satisfies Record<keyof AddressValue, keyof FormState>

// Mirrors MemberService's server-side checks - purely for fast client-side feedback, the server
// is still the source of truth (MemberService.UpsertMyProfileAsync).
const PH_MOBILE_PATTERN = /^(\+63|63|0)9\d{9}$/

function isValidHousePhone(value: string): boolean {
  if (!/^[\d\s\-()]+$/.test(value)) return false
  const digits = value.replace(/\D/g, '')
  return digits.length >= 7 && digits.length <= 11
}

interface ContactInformationSectionProps {
  member: Member
  onUpdated: (member: Member) => void
}

interface FormState {
  housePhone: string
  mobileNumber: string
  houseNo: string
  street: string
  barangay: string
  cityMunicipality: string
  province: string
  zipCode: string
  country: string
  /** Client-only convenience - when true, the mailing inputs are hidden and residence values are
   *  copied into the mailing fields at save time. There's no stored flag; a returning edit infers
   *  the initial state via mailingMirrorsResidence. */
  mailingSameAsResidence: boolean
  mailingHouseNo: string
  mailingStreet: string
  mailingBarangay: string
  mailingCityMunicipality: string
  mailingProvince: string
  mailingZipCode: string
  mailingCountry: string
}

function toFormState(member: Member): FormState {
  return {
    housePhone: member.housePhone ?? '',
    mobileNumber: member.mobileNumber ?? '',
    houseNo: member.houseNo ?? '',
    street: member.street ?? '',
    barangay: member.barangay ?? '',
    cityMunicipality: member.cityMunicipality ?? '',
    province: member.province ?? '',
    zipCode: member.zipCode ?? '',
    country: member.country ?? 'Philippines',
    mailingSameAsResidence: mailingMirrorsResidence(member),
    mailingHouseNo: member.mailingHouseNo ?? '',
    mailingStreet: member.mailingStreet ?? '',
    mailingBarangay: member.mailingBarangay ?? '',
    mailingCityMunicipality: member.mailingCityMunicipality ?? '',
    mailingProvince: member.mailingProvince ?? '',
    mailingZipCode: member.mailingZipCode ?? '',
    mailingCountry: member.mailingCountry ?? 'Philippines',
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

    const mailing = form.mailingSameAsResidence
      ? {
          mailingHouseNo: form.houseNo || null,
          mailingStreet: form.street || null,
          mailingBarangay: form.barangay || null,
          mailingCityMunicipality: form.cityMunicipality || null,
          mailingProvince: form.province || null,
          mailingZipCode: form.zipCode || null,
          mailingCountry: form.country || null,
        }
      : {
          mailingHouseNo: form.mailingHouseNo || null,
          mailingStreet: form.mailingStreet || null,
          mailingBarangay: form.mailingBarangay || null,
          mailingCityMunicipality: form.mailingCityMunicipality || null,
          mailingProvince: form.mailingProvince || null,
          mailingZipCode: form.mailingZipCode || null,
          mailingCountry: form.mailingCountry || null,
        }

    setSaving(true)
    try {
      const updated = await memberApi.updateMyProfile(
        buildFullProfileRequest(member, {
          housePhone: form.housePhone || null,
          mobileNumber: form.mobileNumber || null,
          houseNo: form.houseNo || null,
          street: form.street || null,
          barangay: form.barangay || null,
          cityMunicipality: form.cityMunicipality || null,
          province: form.province || null,
          zipCode: form.zipCode || null,
          country: form.country || null,
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

      <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-3 min-[1800px]:grid-cols-4 gap-4 text-sm">
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">House Phone</span>
          {editing ? (
            <input
              className="form-input"
              placeholder="e.g. (02) 8123 4567"
              value={form.housePhone}
              onChange={(e) => handleChange('housePhone', formatPhLandline(e.target.value))}
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
              onChange={(e) => handleChange('mobileNumber', formatPhMobile(e.target.value))}
            />
          ) : (
            <span className="font-semibold text-default-800">{member.mobileNumber || '-'}</span>
          )}
        </div>
        <div className="md:col-span-2">
          <span className="block font-medium text-default-900 text-sm mb-2">Email Address</span>
          <span className="font-semibold text-default-800">{member.email}</span>
        </div>
      </div>

      <div className="border-t border-default-200 pt-4">
        <h6 className="font-semibold text-default-800 mb-3">Residence Address</h6>
        <PhilippineAddressFields
          idPrefix="residence"
          editing={editing}
          value={{
            houseNo: editing ? form.houseNo : member.houseNo ?? '',
            street: editing ? form.street : member.street ?? '',
            barangay: editing ? form.barangay : member.barangay ?? '',
            cityMunicipality: editing ? form.cityMunicipality : member.cityMunicipality ?? '',
            province: editing ? form.province : member.province ?? '',
            zipCode: editing ? form.zipCode : member.zipCode ?? '',
            country: editing ? form.country : member.country ?? '',
          }}
          onChange={handleChange}
        />
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
          <PhilippineAddressFields
            idPrefix="mailing"
            value={{
              houseNo: form.mailingHouseNo,
              street: form.mailingStreet,
              barangay: form.mailingBarangay,
              cityMunicipality: form.mailingCityMunicipality,
              province: form.mailingProvince,
              zipCode: form.mailingZipCode,
              country: form.mailingCountry,
            }}
            onChange={(field, next) => handleChange(MAILING_FIELD[field], next)}
          />
        ) : !editing ? (
          <PhilippineAddressFields
            idPrefix="mailing"
            editing={false}
            value={{
              houseNo: member.mailingHouseNo ?? '',
              street: member.mailingStreet ?? '',
              barangay: member.mailingBarangay ?? '',
              cityMunicipality: member.mailingCityMunicipality ?? '',
              province: member.mailingProvince ?? '',
              zipCode: member.mailingZipCode ?? '',
              country: member.mailingCountry ?? '',
            }}
            onChange={() => {}}
          />
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
