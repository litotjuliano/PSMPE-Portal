import type { FormEvent } from 'react'
import { Chapters } from '../../../core/types/member'
import type { Member } from '../../../core/types/member'

export interface MyProfileFormState {
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
}

interface MyProfileCardProps {
  /** null until the member has saved a profile for the first time. */
  existing: Member | null
  state: MyProfileFormState
  onChange: <K extends keyof MyProfileFormState>(field: K, value: MyProfileFormState[K]) => void
  onSubmit: (event: FormEvent) => void
}

const statusLabels: Record<number, string> = { 0: 'Pending', 1: 'Active', 2: 'Expired', 3: 'Deactivated' }

export const MyProfileCard = ({ existing, state, onChange, onSubmit }: MyProfileCardProps) => {
  return (
    <div className="flex flex-col gap-4 max-w-3xl">
      {existing && (
        <div className="card">
          <div className="card-body flex flex-wrap gap-x-8 gap-y-2 text-sm">
            <div>
              <span className="text-default-500">Membership No.</span>{' '}
              <span className="font-semibold text-default-800">{existing.membershipNo}</span>
            </div>
            <div>
              <span className="text-default-500">Status</span>{' '}
              <span className="font-semibold text-default-800">{statusLabels[existing.status]}</span>
            </div>
            <div>
              <span className="text-default-500">Email</span> <span className="font-semibold text-default-800">{existing.email}</span>
            </div>
          </div>
        </div>
      )}

      <div className="card">
        <div className="card-header">
          <h6 className="card-title">{existing ? 'My Profile' : 'Complete your profile'}</h6>
        </div>
        <form onSubmit={onSubmit} className="card-body grid grid-cols-1 md:grid-cols-2 gap-4">
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

          <div>
            <label className="block font-medium text-default-900 text-sm mb-2">PRC License No.</label>
            <input
              className="form-input"
              value={state.prcLicenseNo}
              onChange={(e) => onChange('prcLicenseNo', e.target.value)}
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
          <div className="md:col-span-2">
            <label className="block font-medium text-default-900 text-sm mb-2">Company</label>
            <input className="form-input" value={state.company} onChange={(e) => onChange('company', e.target.value)} />
          </div>

          <div className="md:col-span-2 mt-2">
            <button type="submit" className="btn bg-primary text-white">
              Save
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
