import { useEffect, useState } from 'react'
import {
  LuAward,
  LuBanknote,
  LuCalendarCheck,
  LuCalendarClock,
  LuCalendarDays,
  LuCircleCheck,
  LuCreditCard,
  LuGraduationCap,
  LuTimer,
  LuWallet,
} from 'react-icons/lu'
import { memberApi, type ProfileCompleteness } from '../../../core/api/endpoints/memberApi'
import type { Member } from '../../../core/types/member'
import { PersonalInformationSection } from './profile-sections/PersonalInformationSection'
import { ProfessionalLicensingSection } from './profile-sections/ProfessionalLicensingSection'
import { ContactInformationSection } from './profile-sections/ContactInformationSection'
import { AccountInformationSection } from './profile-sections/AccountInformationSection'
import { DocumentsCertificatesSection } from './profile-sections/DocumentsCertificatesSection'
import { ProfileRail } from './profile-cards/ProfileRail'
import { MembershipStatusCard } from './profile-cards/MembershipStatusCard'
import { QuickActionsCard } from './profile-cards/QuickActionsCard'
import { PsmpeJourneyCard } from './profile-cards/PsmpeJourneyCard'
import { ComingSoonCard } from './profile-cards/ComingSoonCard'

interface MyProfileTabsCardProps {
  existing: Member
  onUpdated: (member: Member) => void
  /** Fetched once by MyProfilePage so the photo isn't requested by several cards at once. */
  photoUrl: string | null
  onPhotoChanged: (url: string | null) => void
}

const tabs = ['Personal', 'Professional & Licensing', 'Contact', 'Account & Security', 'Documents & Certificates']

/**
 * Replaces the old always-editable MyProfileCard once an application is submitted (same boundary
 * MyProfilePage already used) - each tab below owns its own View/Edit state and Save action
 * independently, mirroring the 4-step registration wizard.
 *
 * Regrouped from the original 4 tabs (Personal Information / Contact Information / Account
 * Information / Additional Information) into 5, by concern rather than by "when it was added to
 * the form": Personal is now pure identity, Professional & Licensing absorbs education + RMP/PRC
 * licensing + employment (previously split across two different tabs), Account & Security gained
 * the Display Name/Change Password forms that used to live in a separate card below the tabs, and
 * Documents & Certificates was split out of what used to be a grab-bag "Additional Information".
 *
 * Layout runs real content first (identity, status, the detail tabs) and the not-yet-built panels
 * last, so a member doesn't open their profile onto a wall of empty boxes. The page is full-width:
 * the previous max-w-3xl cap meant a 1440px monitor showed 768px of profile and 670px of nothing.
 */
export const MyProfileTabsCard = ({ existing, onUpdated, photoUrl, onPhotoChanged }: MyProfileTabsCardProps) => {
  const [activeTab, setActiveTab] = useState(0)
  const [completeness, setCompleteness] = useState<ProfileCompleteness | null>(null)

  // One fetch shared by the status card's progress bar and the journey card's certificate count.
  useEffect(() => {
    let cancelled = false
    memberApi
      .getMyProfileCompleteness()
      .then((result) => {
        if (!cancelled) setCompleteness(result)
      })
      .catch(() => {
        // Non-critical decoration - both consumers render without it.
      })
    return () => {
      cancelled = true
    }
  }, [existing.updatedAt])

  return (
    <div className="flex flex-col gap-5">
      {!existing.approvedAt && (
        <div className="card border border-info/30 bg-info/10">
          <div className="card-body text-sm font-medium text-info">
            Thank you for registering. Your profile is pending admin approval.
          </div>
        </div>
      )}

      {/* Proportional 3-column row, not a 1/6 rail: Member Profile Summary gets the width its
          tabbed form actually needs (4 of 6 parts), Status and Actions share the rest. Below xl,
          Status+Actions live in their own 2-col grid; `xl:contents` on that wrapper "unwraps" it
          at xl so its two children become direct items of the outer 3-column grid instead of one
          combined track. */}
      <div className="grid grid-cols-1 xl:grid-cols-[4fr_1fr_1fr] gap-5">
        <div className="flex flex-col">
          <div className="card">
            <div className="card-header">
              <h6 className="card-title">Member Profile Summary</h6>
            </div>

            <div className="card-body flex flex-col md:flex-row gap-5">
              {/* Persistent across all 5 tabs - photo and Membership ID used to live inside
                  Personal Information alone, so switching tabs hid them. */}
              <ProfileRail member={existing} photoUrl={photoUrl} onPhotoChanged={onPhotoChanged} />

              <div className="flex-1 min-w-0">
                {/* Scrolls rather than wrapping: at 375px these labels wrap to multiple rows and
                    push the form off screen. */}
                <div className="flex flex-nowrap overflow-x-auto gap-2 mb-6 border-b border-default-200 pb-3 -mx-1 px-1">
                  {tabs.map((label, i) => (
                    <button
                      key={label}
                      type="button"
                      onClick={() => setActiveTab(i)}
                      className={`px-3 py-1.5 rounded-lg text-sm font-medium transition whitespace-nowrap shrink-0 ${
                        i === activeTab ? 'bg-primary text-white' : 'text-default-600 hover:bg-default-150'
                      }`}
                    >
                      {label}
                    </button>
                  ))}
                </div>

                {activeTab === 0 && <PersonalInformationSection member={existing} onUpdated={onUpdated} />}
                {activeTab === 1 && <ProfessionalLicensingSection member={existing} onUpdated={onUpdated} />}
                {activeTab === 2 && <ContactInformationSection member={existing} onUpdated={onUpdated} />}
                {activeTab === 3 && <AccountInformationSection />}
                {activeTab === 4 && <DocumentsCertificatesSection />}
              </div>
            </div>
          </div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 xl:contents gap-5">
          <MembershipStatusCard member={existing} completeness={completeness} />
          <QuickActionsCard onGoToTab={setActiveTab} />
        </div>
      </div>

      <PsmpeJourneyCard member={existing} completeness={completeness} />

      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5">
        <ComingSoonCard
          title="Account Summary"
          icon={LuWallet}
          items={[
            { label: 'Subscription Plan', icon: LuCreditCard },
            { label: 'Payment Status', icon: LuCircleCheck },
            { label: 'Outstanding Balance', icon: LuBanknote },
            { label: 'Last Payment Date', icon: LuCalendarClock },
          ]}
          message="Billing and payment history will appear here once online payments are available."
        />
        <ComingSoonCard
          title="Professional Activities"
          icon={LuCalendarCheck}
          items={[
            { label: 'Events Attended', icon: LuCalendarDays },
            { label: 'Seminars Completed', icon: LuGraduationCap },
            { label: 'CPD Credits Earned', icon: LuTimer },
            { label: 'Training Hours', icon: LuTimer },
          ]}
          message="Attendance and CPD credits will appear here once event tracking is available."
        />
        <ComingSoonCard
          title="My Badges"
          icon={LuAward}
          message="Achievement badges will appear here once the recognition programme launches."
        />
      </div>
    </div>
  )
}
