import { useEffect, useState, type FormEvent } from 'react'
import { LuEye, LuSquarePen, LuUserRound } from 'react-icons/lu'
import {
  Chapters,
  CivilStatuses,
  EducationLevels,
  EmploymentStatuses,
  MemberTypes,
  MembershipStatus,
  SpecifiedProfessions,
  type MembershipStatusValue,
} from '../../../core/types/member'
import {
  CHAPTER_YEAR_MAX,
  CHAPTER_YEAR_MIN,
  deriveRmpValidUntil,
  formatPhLandline,
  formatPhMobile,
  shouldDeriveValidUntil,
} from '../../../core/utils/memberFields'
import type { UserSummary } from '../../../core/api/endpoints/adminApi'
import type { ProfileCompleteness } from '../../../core/api/endpoints/memberApi'
import { uploadApi } from '../../../core/api/endpoints/uploadApi'
import { StandardButton } from '../components/shared/StandardButton'
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
} as const satisfies Record<keyof AddressValue, keyof MemberFormState>

export interface MemberFormState {
  userId: string
  membershipNo: string
  firstName: string
  middleName: string
  lastName: string
  suffix: string
  birthdate: string
  gender: string
  civilStatus: string
  educationLevel: string
  schoolName: string
  courseYearGraduated: string
  specifiedProfession: string
  houseNo: string
  street: string
  barangay: string
  cityMunicipality: string
  province: string
  zipCode: string
  country: string
  mailingHouseNo: string
  mailingStreet: string
  mailingBarangay: string
  mailingCityMunicipality: string
  mailingProvince: string
  mailingZipCode: string
  mailingCountry: string
  mobileNumber: string
  housePhone: string
  prcLicenseNo: string
  prcRegistrationDate: string
  prcValidUntilDate: string
  ptrNumber: string
  ptrPlaceIssued: string
  ptrDateIssued: string
  tin: string
  chapter: string
  chapterYear: string
  chapterPosition: string
  employmentStatus: string
  company: string
  position: string
  businessAddress: string
  yearsOfPractice: string
  specialization: string
  skills: string
  memberType: string
  status: MembershipStatusValue
  renewalDueDate: string
  nationalDuesReferenceNo: string
}

interface MemberFormCardProps {
  isNew: boolean
  state: MemberFormState
  onChange: <K extends keyof MemberFormState>(field: K, value: MemberFormState[K]) => void
  onSubmit: (event: FormEvent) => void
  /** Only used when isNew - candidate login accounts this Member profile can be linked to. */
  users: UserSummary[]
  /** The Member's own Id (route param) - not userId - used to fetch this member's uploaded
   *  documents (photo, RMP ID, etc.) for the view-mode previews below. Only present when editing
   *  an existing member. */
  memberId?: string
  /** Only present when editing an existing, not-yet-approved application - lets an admin act
   *  right from this page, which is where a notification click lands them. */
  approvedAt?: string | null
  onApprove?: () => void
  isInGracePeriod?: boolean
  /** Only present when editing an existing member - surfaces which ID-issuance documents (photo,
   *  signature, valid ID) are still missing, so staff don't have to open My Profile to check. */
  completeness?: ProfileCompleteness | null
}

function DocumentCompletenessRow({
  label,
  present,
  onView,
}: {
  label: string
  present: boolean
  /** Only rendered when the document is actually present - nothing to view otherwise. */
  onView?: () => void
}) {
  return (
    <div className="flex items-center justify-between text-sm">
      <span className="text-default-600">{label}</span>
      <div className="flex items-center gap-3">
        {present && onView && (
          <StandardButton variant="view" size="sm" icon={LuEye} onClick={onView}>
            View {label}
          </StandardButton>
        )}
        {!present && <span className="text-danger font-medium">Missing</span>}
      </div>
    </div>
  )
}

/** Plain label/value pair used in view mode - falls back to '-' when empty, same convention the
 *  profile-sections components already use for the member's own read-only views. */
function ViewField({ label, value, colSpan }: { label: string; value: string | number | null | undefined; colSpan?: boolean }) {
  return (
    <div className={colSpan ? 'md:col-span-2' : undefined}>
      <span className="block font-medium text-default-900 text-sm mb-2">{label}</span>
      <span className="font-semibold text-default-800">{value || '-'}</span>
    </div>
  )
}

const statusLabels: Record<MembershipStatusValue, string> = {
  [MembershipStatus.Pending]: 'Pending',
  [MembershipStatus.Active]: 'Active',
  [MembershipStatus.Expired]: 'Expired',
  [MembershipStatus.Deactivated]: 'Deactivated',
}

type DocumentPreview = 'photo' | 'prcId' | 'validGovernmentId' | 'proofOfPayment'

export const MemberFormCard = ({
  isNew,
  state,
  onChange,
  onSubmit,
  users,
  memberId,
  approvedAt,
  onApprove,
  isInGracePeriod,
  completeness,
}: MemberFormCardProps) => {
  // Existing members open in read-only view mode, same as the member's own profile pages - a
  // reviewer landing here (e.g. from a notification, to approve an application) sees a display of
  // what was submitted, not a raw editable form. isNew has nothing to view yet, so it always
  // starts in edit mode.
  const [editing, setEditing] = useState(isNew)
  const [photoPreviewUrl, setPhotoPreviewUrl] = useState<string | null>(null)
  const [previewOpen, setPreviewOpen] = useState<DocumentPreview | null>(null)

  useEffect(() => {
    if (!memberId) return
    let cancelled = false
    uploadApi.fetchMemberPhotoUrl(memberId).then((result) => {
      if (!cancelled && result) setPhotoPreviewUrl(result.url)
    })
    return () => {
      cancelled = true
    }
  }, [memberId])

  useEffect(() => {
    return () => {
      if (photoPreviewUrl) URL.revokeObjectURL(photoPreviewUrl)
    }
  }, [photoPreviewUrl])

  return (
    <div className="card max-w-3xl">
      <div className="card-header flex items-center justify-between">
        <h6 className="card-title">{isNew ? 'New member profile' : 'Edit member profile'}</h6>
        <div className="flex items-center gap-2">
          {!isNew && !approvedAt && onApprove && (
            <button type="button" onClick={onApprove} className="btn btn-sm bg-success text-white">
              Approve Application
            </button>
          )}
          {!isNew && !editing && (
            <StandardButton size="sm" variant="on-primary" icon={LuSquarePen} onClick={() => setEditing(true)}>
              Edit
            </StandardButton>
          )}
        </div>
      </div>
      {!isNew && (approvedAt || isInGracePeriod) && (
        <div className="px-5 pt-4 flex flex-col gap-1 text-sm">
          {approvedAt && (
            <div>
              <span className="text-default-500">Approved</span>{' '}
              <span className="font-semibold text-default-800">{new Date(approvedAt).toLocaleDateString()}</span>
            </div>
          )}
          {isInGracePeriod && (
            <div className="text-warning font-medium">
              This member is past their renewal due date and is currently within the grace period.
            </div>
          )}
        </div>
      )}
      {!isNew && completeness && (
        <div className="px-5 pt-4">
          <div className="border border-default-200 rounded-lg p-4 flex flex-col gap-2">
            <h6 className="font-semibold text-default-800 text-sm">Document completeness ({completeness.percentComplete}%)</h6>
            <DocumentCompletenessRow label="RMP ID" present={completeness.hasPrcId} onView={() => setPreviewOpen('prcId')} />
            <DocumentCompletenessRow
              label="Proof of Payment"
              present={completeness.hasProofOfPayment}
              onView={() => setPreviewOpen('proofOfPayment')}
            />
            <DocumentCompletenessRow label="Photo" present={completeness.hasPhoto} onView={() => setPreviewOpen('photo')} />
          </div>
        </div>
      )}

      {!editing ? (
        <div className="card-body grid grid-cols-1 md:grid-cols-2 gap-4">
          <div className="md:col-span-2 flex items-center gap-4">
            <div className="size-20 rounded-full bg-default-150 flex items-center justify-center overflow-hidden shrink-0">
              {photoPreviewUrl ? (
                <img src={photoPreviewUrl} alt="Profile" className="size-full object-cover" />
              ) : (
                <LuUserRound className="size-10 text-default-400" />
              )}
            </div>
          </div>

          <ViewField label="First Name" value={state.firstName} />
          <ViewField label="Last Name" value={state.lastName} />
          <ViewField label="Middle Name" value={state.middleName} />
          <ViewField label="Suffix" value={state.suffix} />

          <ViewField label="Birthdate" value={state.birthdate} />
          <ViewField label="Gender" value={state.gender} />
          <ViewField label="Civil Status" value={state.civilStatus} />
          <ViewField label="Mobile Number" value={state.mobileNumber} />
          <ViewField label="House Phone" value={state.housePhone} />

          <ViewField label="Educational Record" value={state.educationLevel} />
          <ViewField label="Name of School/Institution" value={state.schoolName} />
          <ViewField label="Course & Year Graduated" value={state.courseYearGraduated} />
          <ViewField label="Specified Profession" value={state.specifiedProfession} />

          <div className="md:col-span-2 border-t border-default-200 pt-4">
            <h6 className="font-semibold text-default-800 text-sm">Residence Address</h6>
          </div>
          <ViewField label="House No." value={state.houseNo} />
          <ViewField label="Street" value={state.street} />
          <ViewField label="Barangay" value={state.barangay} />
          <ViewField label="City or Municipality" value={state.cityMunicipality} />
          <ViewField label="Province" value={state.province} />
          <ViewField label="Zip Code" value={state.zipCode} />
          <ViewField label="Country" value={state.country} />

          <div className="md:col-span-2 border-t border-default-200 pt-4">
            <h6 className="font-semibold text-default-800 text-sm">Mailing Address</h6>
          </div>
          <ViewField label="House No." value={state.mailingHouseNo} />
          <ViewField label="Street" value={state.mailingStreet} />
          <ViewField label="Barangay" value={state.mailingBarangay} />
          <ViewField label="City or Municipality" value={state.mailingCityMunicipality} />
          <ViewField label="Province" value={state.mailingProvince} />
          <ViewField label="Zip Code" value={state.mailingZipCode} />
          <ViewField label="Country" value={state.mailingCountry} />

          <ViewField label="RMP License No." value={state.prcLicenseNo} />
          <ViewField label="RMP Registration Date" value={state.prcRegistrationDate} />
          <ViewField label="RMP Valid Until" value={state.prcValidUntilDate} />
          <ViewField label="PTR Number" value={state.ptrNumber} />
          <ViewField label="PTR Place Issued" value={state.ptrPlaceIssued} />
          <ViewField label="PTR Date Issued" value={state.ptrDateIssued} />
          <ViewField label="TIN" value={state.tin} />
          <ViewField label="Chapter" value={state.chapter} />
          <ViewField label="Chapter Officer Year" value={state.chapterYear} />
          <ViewField label="Chapter Position" value={state.chapterPosition} />

          <ViewField label="Employment Status" value={state.employmentStatus} />
          <ViewField label="Company" value={state.company} />
          <ViewField label="Position" value={state.position} />
          <ViewField label="Business Address" value={state.businessAddress} colSpan />
          <ViewField label="Years of Practice" value={state.yearsOfPractice} />
          <ViewField label="Specialization" value={state.specialization} />
          <ViewField label="Skills" value={state.skills} colSpan />

          <ViewField label="Member Type" value={state.memberType} />
          <ViewField label="Membership Status" value={statusLabels[state.status]} />
          <ViewField label="Renewal Due Date" value={state.renewalDueDate} />
          <ViewField label="National Dues Reference No." value={state.nationalDuesReferenceNo} />

          {memberId && completeness?.hasValidGovernmentId && (
            <div className="md:col-span-2 flex flex-wrap gap-3 border-t border-default-200 pt-4">
              <StandardButton variant="view" size="sm" icon={LuEye} onClick={() => setPreviewOpen('validGovernmentId')}>
                View Valid Government ID
              </StandardButton>
            </div>
          )}
        </div>
      ) : (
        <form onSubmit={onSubmit} className="card-body grid grid-cols-1 md:grid-cols-2 gap-4">
          {isNew && (
            <div className="md:col-span-2">
              <label className="block font-medium text-default-900 text-sm mb-2">Linked login account</label>
              <select className="form-input" required value={state.userId} onChange={(e) => onChange('userId', e.target.value)}>
                <option value="">Select a user…</option>
                {users.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.displayName} ({u.email})
                  </option>
                ))}
              </select>
            </div>
          )}

          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">First Name</label>
            <input className="form-input" required value={state.firstName} onChange={(e) => onChange('firstName', e.target.value)} />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Last Name</label>
            <input className="form-input" required value={state.lastName} onChange={(e) => onChange('lastName', e.target.value)} />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Middle Name</label>
            <input className="form-input" value={state.middleName} onChange={(e) => onChange('middleName', e.target.value)} />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Suffix</label>
            <input className="form-input" value={state.suffix} onChange={(e) => onChange('suffix', e.target.value)} />
          </div>

          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Birthdate</label>
            <input
              type="date"
              className="form-input"
              value={state.birthdate}
              onChange={(e) => onChange('birthdate', e.target.value)}
            />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Gender</label>
            <input className="form-input" value={state.gender} onChange={(e) => onChange('gender', e.target.value)} />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Civil Status</label>
            <select className="form-input" value={state.civilStatus} onChange={(e) => onChange('civilStatus', e.target.value)}>
              <option value="">Select civil status…</option>
              {Object.values(CivilStatuses).map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Mobile Number</label>
            <input
              className="form-input"
              placeholder="09XXXXXXXXX"
              value={state.mobileNumber}
              onChange={(e) => onChange('mobileNumber', formatPhMobile(e.target.value))}
            />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">House Phone</label>
            <input
              className="form-input"
              placeholder="e.g. (02) 8123 4567"
              value={state.housePhone}
              onChange={(e) => onChange('housePhone', formatPhLandline(e.target.value))}
            />
          </div>

          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Educational Record</label>
            <select
              className="form-input"
              value={state.educationLevel}
              onChange={(e) => onChange('educationLevel', e.target.value)}
            >
              <option value="">Select education level…</option>
              {Object.values(EducationLevels).map((level) => (
                <option key={level} value={level}>
                  {level}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Name of School/Institution</label>
            <input className="form-input" value={state.schoolName} onChange={(e) => onChange('schoolName', e.target.value)} />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Course &amp; Year Graduated</label>
            <input
              className="form-input"
              placeholder="e.g. BSCE 2023"
              value={state.courseYearGraduated}
              onChange={(e) => onChange('courseYearGraduated', e.target.value)}
            />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Specified Profession</label>
            <select
              className="form-input"
              value={state.specifiedProfession}
              onChange={(e) => onChange('specifiedProfession', e.target.value)}
            >
              <option value="">Select profession…</option>
              {Object.values(SpecifiedProfessions).map((profession) => (
                <option key={profession} value={profession}>
                  {profession}
                </option>
              ))}
            </select>
          </div>

          <div className="md:col-span-2 border-t border-default-200 pt-4">
            <h6 className="font-semibold text-default-800 text-sm">Residence Address</h6>
          </div>
          {/* No `required` here - admin edits are deliberately looser than the self-service
              wizard, same as these fields have always been on this form. */}
          <PhilippineAddressFields
            idPrefix="admin-residence"
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

          <div className="md:col-span-2 border-t border-default-200 pt-4">
            <h6 className="font-semibold text-default-800 text-sm">Mailing Address</h6>
          </div>
          <PhilippineAddressFields
            idPrefix="admin-mailing"
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

          {/* Editable after create too - this is the correction path for a control number
              mistyped at approval. Not required: PSMPE assigns it, and an admin creating a profile
              may not have it yet. Leaving it blank on an existing member keeps the stored value. */}
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Membership ID</label>
            <input
              className="form-input"
              maxLength={32}
              value={state.membershipNo}
              onChange={(e) => onChange('membershipNo', e.target.value)}
            />
            <p className="text-xs text-default-500 mt-1">
              {isNew ? 'Optional - assigned at approval if left blank.' : "PSMPE's membership control number."}
            </p>
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">RMP License No.</label>
            {/* Required: without a licence number a member never reaches the verification queue,
                and approval now requires verification. See MemberService.CreateAsync. */}
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
              value={state.prcRegistrationDate}
              onChange={(e) => {
                if (shouldDeriveValidUntil(state.prcValidUntilDate, state.prcRegistrationDate)) {
                  onChange('prcValidUntilDate', deriveRmpValidUntil(e.target.value))
                }
                onChange('prcRegistrationDate', e.target.value)
              }}
            />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">RMP Valid Until</label>
            <input
              type="date"
              className="form-input"
              value={state.prcValidUntilDate}
              onChange={(e) => onChange('prcValidUntilDate', e.target.value)}
            />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">PTR Number</label>
            <input className="form-input" value={state.ptrNumber} onChange={(e) => onChange('ptrNumber', e.target.value)} />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">PTR Place Issued</label>
            <input
              className="form-input"
              placeholder="e.g. Quezon City"
              value={state.ptrPlaceIssued}
              onChange={(e) => onChange('ptrPlaceIssued', e.target.value)}
            />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">PTR Date Issued</label>
            <input
              className="form-input"
              type="date"
              value={state.ptrDateIssued}
              onChange={(e) => onChange('ptrDateIssued', e.target.value)}
            />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">TIN</label>
            <input
              className="form-input"
              placeholder="000-000-000-000"
              value={state.tin}
              onChange={(e) => onChange('tin', e.target.value)}
            />
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
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Chapter Officer Year</label>
            <input
              className="form-input"
              type="number"
              min={CHAPTER_YEAR_MIN}
              max={CHAPTER_YEAR_MAX}
              placeholder="e.g. 2024"
              value={state.chapterYear}
              onChange={(e) => onChange('chapterYear', e.target.value)}
            />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Chapter Position</label>
            <input
              className="form-input"
              placeholder="e.g. Secretary"
              value={state.chapterPosition}
              onChange={(e) => onChange('chapterPosition', e.target.value)}
            />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Employment Status</label>
            <select
              className="form-input"
              value={state.employmentStatus}
              onChange={(e) => onChange('employmentStatus', e.target.value)}
            >
              <option value="">Select employment status…</option>
              {Object.values(EmploymentStatuses).map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Company</label>
            <input className="form-input" value={state.company} onChange={(e) => onChange('company', e.target.value)} />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Position</label>
            <input className="form-input" value={state.position} onChange={(e) => onChange('position', e.target.value)} />
          </div>
          <div className="md:col-span-2">
            <label className="block font-medium text-default-900 text-sm mb-2">Business Address</label>
            <input className="form-input" value={state.businessAddress} onChange={(e) => onChange('businessAddress', e.target.value)} />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Years of Practice</label>
            <input
              type="number"
              min={0}
              className="form-input"
              value={state.yearsOfPractice}
              onChange={(e) => onChange('yearsOfPractice', e.target.value)}
            />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Specialization</label>
            <input className="form-input" value={state.specialization} onChange={(e) => onChange('specialization', e.target.value)} />
          </div>
          <div className="md:col-span-2">
            <label className="block font-medium text-default-900 text-sm mb-2">Skills</label>
            <input className="form-input" value={state.skills} onChange={(e) => onChange('skills', e.target.value)} />
          </div>
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

          {!isNew && (
            <div>
              <label className="block font-medium text-default-900 text-sm mb-2">Membership Status</label>
              <select
                className="form-input"
                value={state.status}
                onChange={(e) => onChange('status', Number(e.target.value) as MembershipStatusValue)}
              >
                {Object.entries(statusLabels).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </div>
          )}

          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Renewal Due Date</label>
            <input
              type="date"
              className="form-input"
              value={state.renewalDueDate}
              onChange={(e) => onChange('renewalDueDate', e.target.value)}
            />
          </div>
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">National Dues Reference No.</label>
            <input
              className="form-input"
              value={state.nationalDuesReferenceNo}
              onChange={(e) => onChange('nationalDuesReferenceNo', e.target.value)}
            />
          </div>

          <div className="md:col-span-2 mt-2 flex items-center gap-2">
            <button type="submit" className="btn bg-primary text-white">
              Save
            </button>
            {!isNew && (
              <button type="button" onClick={() => setEditing(false)} className="btn border border-default-200">
                Cancel
              </button>
            )}
          </div>
        </form>
      )}

      {memberId && (
        <>
          <FilePreviewModal
            isOpen={previewOpen === 'photo'}
            title="Photo"
            fetchFile={() => uploadApi.fetchMemberPhotoUrl(memberId)}
            onClose={() => setPreviewOpen(null)}
          />
          <FilePreviewModal
            isOpen={previewOpen === 'prcId'}
            title="RMP ID"
            fetchFile={() => uploadApi.fetchMemberPrcIdUrl(memberId)}
            onClose={() => setPreviewOpen(null)}
          />
          <FilePreviewModal
            isOpen={previewOpen === 'validGovernmentId'}
            title="Valid Government ID"
            fetchFile={() => uploadApi.fetchMemberValidGovernmentIdUrl(memberId)}
            onClose={() => setPreviewOpen(null)}
          />
          <FilePreviewModal
            isOpen={previewOpen === 'proofOfPayment'}
            title="Proof of Payment"
            fetchFile={() => uploadApi.fetchMemberProofOfPaymentUrl(memberId)}
            onClose={() => setPreviewOpen(null)}
          />
        </>
      )}
    </div>
  )
}
