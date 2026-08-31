import { LuBadgeCheck, LuCircleAlert, LuClock, LuCircleSlash } from 'react-icons/lu'
import type { IconType } from 'react-icons/lib'
import type { ProfileCompleteness } from '../../../../core/api/endpoints/memberApi'
import type { Member } from '../../../../core/types/member'
import { MembershipStatus } from '../../../../core/types/member'

interface MembershipStatusCardProps {
  member: Member
  completeness: ProfileCompleteness | null
}

const statusDisplay: Record<number, { title: string; blurb: string; icon: IconType; tone: string; tint: string }> = {
  [MembershipStatus.Pending]: {
    title: 'Pending Approval',
    blurb: 'Your application has been received and is awaiting review by an administrator.',
    icon: LuClock,
    tone: 'text-warning',
    tint: 'bg-warning/10',
  },
  [MembershipStatus.Active]: {
    title: 'Active Member',
    blurb: 'Your membership is active and in good standing.',
    icon: LuBadgeCheck,
    tone: 'text-success',
    tint: 'bg-success/10',
  },
  [MembershipStatus.Expired]: {
    title: 'Expired',
    blurb: 'Your membership has lapsed. Renew to restore your benefits.',
    icon: LuCircleAlert,
    tone: 'text-danger',
    tint: 'bg-danger/10',
  },
  [MembershipStatus.Deactivated]: {
    title: 'Deactivated',
    blurb: 'This membership has been deactivated. Contact the national office for help.',
    icon: LuCircleSlash,
    tone: 'text-danger',
    tint: 'bg-danger/10',
  },
}

/**
 * Membership standing plus the one genuinely-measured percentage in the system.
 *
 * The design shows "Valid Until" and "Next Renewal Date" as two figures, but they are the same
 * column (Member.RenewalDueDate) - showing it twice under different labels would invent a
 * distinction that doesn't exist, so it appears once. The progress bar is profile completeness
 * (/api/members/me/completeness), not "membership progress", which nothing computes.
 */
export const MembershipStatusCard = ({ member, completeness }: MembershipStatusCardProps) => {
  const display = statusDisplay[member.status] ?? statusDisplay[MembershipStatus.Pending]
  const Icon = display.icon
  const percent = completeness?.percentComplete ?? null

  return (
    <div className="card h-full">
      <div className="card-header">
        <h6 className="card-title">Membership Status</h6>
      </div>
      <div className="card-body flex flex-col gap-4">
        <div className={`flex items-start gap-3 rounded-lg p-3 ${display.tint}`}>
          <Icon className={`size-8 shrink-0 ${display.tone}`} />
          <div className="min-w-0">
            <p className={`font-semibold ${display.tone}`}>{display.title}</p>
            <p className="text-sm text-default-600 mt-0.5">{display.blurb}</p>
          </div>
        </div>

        <div className="border-t border-default-200 pt-4">
          <span className="block text-xs text-default-500 mb-1">Renewal Due</span>
          <span className="font-semibold text-default-800">
            {member.renewalDueDate ? new Date(member.renewalDueDate).toLocaleDateString() : 'Not yet scheduled'}
          </span>
        </div>

        {member.isInGracePeriod && (
          <p className="text-sm text-warning bg-warning/10 rounded-lg px-3 py-2">
            Your membership is past its renewal due date and is currently within the grace period.
          </p>
        )}

        {percent !== null && (
          <div className="border-t border-default-200 pt-4">
            <div className="flex items-center justify-between gap-3 mb-2">
              <span className="text-xs text-default-500">Profile Completeness</span>
              <span className="text-xs font-semibold text-default-800">{percent}%</span>
            </div>
            <div
              className="h-2 w-full rounded-full bg-default-150 overflow-hidden"
              role="progressbar"
              aria-valuenow={percent}
              aria-valuemin={0}
              aria-valuemax={100}
              aria-label="Profile completeness"
            >
              <div className="h-full rounded-full bg-teal transition-all" style={{ width: `${percent}%` }} />
            </div>
            {percent < 100 && (
              <p className="text-xs text-default-500 mt-2">
                Add your remaining professional details and documents to reach 100%.
              </p>
            )}
          </div>
        )}
      </div>
    </div>
  )
}
