import type { FormEvent } from 'react'
import { Chapters, MembershipStatus, type MembershipStatusValue } from '../../../core/types/member'
import type { UserSummary } from '../../../core/api/endpoints/adminApi'

export interface MemberFormState {
  userId: string
  membershipNo: string
  firstName: string
  middleName: string
  lastName: string
  suffix: string
  birthdate: string
  gender: string
  address: string
  prcLicenseNo: string
  chapter: string
  company: string
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
}

const statusLabels: Record<MembershipStatusValue, string> = {
  [MembershipStatus.Pending]: 'Pending',
  [MembershipStatus.Active]: 'Active',
  [MembershipStatus.Expired]: 'Expired',
  [MembershipStatus.Deactivated]: 'Deactivated',
}

export const MemberFormCard = ({ isNew, state, onChange, onSubmit, users }: MemberFormCardProps) => {
  return (
    <div className="card max-w-3xl">
      <div className="card-header">
        <h6 className="card-title">{isNew ? 'New member profile' : 'Edit member profile'}</h6>
      </div>
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

        <div className="md:col-span-2">
          <label className="block font-medium text-default-900 text-sm mb-2">Address</label>
          <input className="form-input" value={state.address} onChange={(e) => onChange('address', e.target.value)} />
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
          <label className="block font-medium text-default-900 text-sm mb-2">Company</label>
          <input className="form-input" value={state.company} onChange={(e) => onChange('company', e.target.value)} />
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
