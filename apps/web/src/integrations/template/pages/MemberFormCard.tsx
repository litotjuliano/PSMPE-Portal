import type { FormEvent } from 'react'
import {
  Chapters,
  CivilStatuses,
  EmploymentStatuses,
  MemberTypes,
  MembershipStatus,
  type MembershipStatusValue,
} from '../../../core/types/member'
import type { UserSummary } from '../../../core/api/endpoints/adminApi'
import type { ProfileCompleteness } from '../../../core/api/endpoints/memberApi'

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
  address: string
  mobileNumber: string
  housePhone: string
  website: string
  facebookUrl: string
  linkedInUrl: string
  xUrl: string
  instagramUrl: string
  prcLicenseNo: string
  ptrNumber: string
  tin: string
  chapter: string
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
  /** Only present when editing an existing, not-yet-approved application - lets an admin act
   *  right from this page, which is where a notification click lands them. */
  approvedAt?: string | null
  onApprove?: () => void
  isInGracePeriod?: boolean
  /** Only present when editing an existing member - surfaces which ID-issuance documents (2x2,
   *  signature, valid ID) are still missing, so staff don't have to open My Profile to check. */
  completeness?: ProfileCompleteness | null
}

function DocumentCompletenessRow({ label, present }: { label: string; present: boolean }) {
  return (
    <div className="flex items-center justify-between text-sm">
      <span className="text-default-600">{label}</span>
      <span className={present ? 'text-success font-medium' : 'text-danger font-medium'}>{present ? 'Uploaded' : 'Missing'}</span>
    </div>
  )
}

const statusLabels: Record<MembershipStatusValue, string> = {
  [MembershipStatus.Pending]: 'Pending',
  [MembershipStatus.Active]: 'Active',
  [MembershipStatus.Expired]: 'Expired',
  [MembershipStatus.Deactivated]: 'Deactivated',
}

export const MemberFormCard = ({
  isNew,
  state,
  onChange,
  onSubmit,
  users,
  approvedAt,
  onApprove,
  isInGracePeriod,
  completeness,
}: MemberFormCardProps) => {
  return (
    <div className="card max-w-3xl">
      <div className="card-header flex items-center justify-between">
        <h6 className="card-title">{isNew ? 'New member profile' : 'Edit member profile'}</h6>
        {!isNew && !approvedAt && onApprove && (
          <button type="button" onClick={onApprove} className="btn btn-sm bg-success text-white">
            Approve Application
          </button>
        )}
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
            <DocumentCompletenessRow label="PRC ID" present={completeness.hasPrcId} />
            <DocumentCompletenessRow label="Valid Government ID" present={completeness.hasValidGovernmentId} />
            <DocumentCompletenessRow label="2x2 Formal Photo" present={completeness.hasFormalPhoto} />
            <DocumentCompletenessRow label="Signature" present={completeness.hasSignature} />
            <div className="flex items-center justify-between text-sm">
              <span className="text-default-600">Certificates</span>
              <span className="font-medium text-default-800">{completeness.certificateCount}</span>
            </div>
          </div>
        </div>
      )}
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
            onChange={(e) => onChange('mobileNumber', e.target.value)}
          />
        </div>
        <div>
          <label className="block font-medium text-default-900 text-sm mb-2">House Phone</label>
          <input
            className="form-input"
            placeholder="e.g. (02) 8123 4567"
            value={state.housePhone}
            onChange={(e) => onChange('housePhone', e.target.value)}
          />
        </div>

        <div className="md:col-span-2">
          <label className="block font-medium text-default-900 text-sm mb-2">Address</label>
          <input className="form-input" value={state.address} onChange={(e) => onChange('address', e.target.value)} />
        </div>

        <div>
          <label className="block font-medium text-default-900 text-sm mb-2">Website</label>
          <input
            className="form-input"
            placeholder="https://example.com"
            value={state.website}
            onChange={(e) => onChange('website', e.target.value)}
          />
        </div>
        <div>
          <label className="block font-medium text-default-900 text-sm mb-2">Facebook</label>
          <input className="form-input" value={state.facebookUrl} onChange={(e) => onChange('facebookUrl', e.target.value)} />
        </div>
        <div>
          <label className="block font-medium text-default-900 text-sm mb-2">LinkedIn</label>
          <input className="form-input" value={state.linkedInUrl} onChange={(e) => onChange('linkedInUrl', e.target.value)} />
        </div>
        <div>
          <label className="block font-medium text-default-900 text-sm mb-2">X</label>
          <input className="form-input" value={state.xUrl} onChange={(e) => onChange('xUrl', e.target.value)} />
        </div>
        <div>
          <label className="block font-medium text-default-900 text-sm mb-2">Instagram</label>
          <input className="form-input" value={state.instagramUrl} onChange={(e) => onChange('instagramUrl', e.target.value)} />
        </div>

        {isNew && (
          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">Membership No.</label>
            <input
              className="form-input"
              required
              value={state.membershipNo}
              onChange={(e) => onChange('membershipNo', e.target.value)}
            />
          </div>
        )}
        <div>
          <label className="block font-medium text-default-900 text-sm mb-2">PRC License No.</label>
          <input className="form-input" value={state.prcLicenseNo} onChange={(e) => onChange('prcLicenseNo', e.target.value)} />
        </div>
        <div>
          <label className="block font-medium text-default-900 text-sm mb-2">PTR Number</label>
          <input className="form-input" value={state.ptrNumber} onChange={(e) => onChange('ptrNumber', e.target.value)} />
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

        <div className="md:col-span-2 mt-2">
          <button type="submit" className="btn bg-primary text-white">
            Save
          </button>
        </div>
      </form>
    </div>
  )
}
