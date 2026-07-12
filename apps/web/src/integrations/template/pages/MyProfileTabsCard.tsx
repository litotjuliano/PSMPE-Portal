import { useState } from 'react'
import type { Member } from '../../../core/types/member'
import { PersonalInformationSection } from './profile-sections/PersonalInformationSection'
import { ContactInformationSection } from './profile-sections/ContactInformationSection'
import { AccountInformationSection } from './profile-sections/AccountInformationSection'
import { AdditionalInformationSection } from './profile-sections/AdditionalInformationSection'

interface MyProfileTabsCardProps {
  existing: Member
  accountDisplayName: string
  onUpdated: (member: Member) => void
}

const statusLabels: Record<number, string> = { 0: 'Pending', 1: 'Active', 2: 'Expired', 3: 'Deactivated' }
const tabs = ['Personal Information', 'Contact Information', 'Account Information', 'Additional Information']

/**
 * Replaces the old always-editable MyProfileCard once an application is submitted (same boundary
 * MyProfilePage already used) - each tab below owns its own View/Edit state and Save action
 * independently, per the post-approval profile-continuity change.
 */
export const MyProfileTabsCard = ({ existing, accountDisplayName, onUpdated }: MyProfileTabsCardProps) => {
  const [activeTab, setActiveTab] = useState(0)

  return (
    <div className="flex flex-col gap-4 max-w-3xl">
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
          <div>
            <span className="text-default-500">Member Type</span>{' '}
            <span className="font-semibold text-default-800">{existing.memberType}</span>
          </div>
          <div>
            <span className="text-default-500">Chapter</span> <span className="font-semibold text-default-800">{existing.chapter}</span>
          </div>
        </div>
        {existing.isInGracePeriod && (
          <div className="card-body pt-0 text-sm text-warning font-medium">
            Your membership is past its renewal due date and is currently within the grace period.
          </div>
        )}
      </div>

      <div className="card">
        <div className="card-body">
          <div className="flex flex-wrap gap-2 mb-6 border-b border-default-200 pb-3">
            {tabs.map((label, i) => (
              <button
                key={label}
                type="button"
                onClick={() => setActiveTab(i)}
                className={`px-3 py-1.5 rounded-lg text-sm font-medium transition ${
                  i === activeTab ? 'bg-primary text-white' : 'text-default-600 hover:bg-default-150'
                }`}
              >
                {label}
              </button>
            ))}
          </div>

          {activeTab === 0 && <PersonalInformationSection member={existing} onUpdated={onUpdated} />}
          {activeTab === 1 && <ContactInformationSection member={existing} onUpdated={onUpdated} />}
          {activeTab === 2 && <AccountInformationSection member={existing} accountDisplayName={accountDisplayName} />}
          {activeTab === 3 && <AdditionalInformationSection member={existing} onUpdated={onUpdated} />}
        </div>
      </div>
    </div>
  )
}
