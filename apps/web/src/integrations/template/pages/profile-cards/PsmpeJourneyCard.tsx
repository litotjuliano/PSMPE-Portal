import { LuAward, LuCalendarDays, LuFileBadge, LuHandHeart, LuPresentation, LuTrophy, LuUsers } from 'react-icons/lu'
import type { ProfileCompleteness } from '../../../../core/api/endpoints/memberApi'
import type { Member } from '../../../../core/types/member'
import { StatTile } from '../../components/shared/StatTile'

interface PsmpeJourneyCardProps {
  member: Member
  completeness: ProfileCompleteness | null
}

/**
 * Milestone strip. Three tiles are real: Member Since and Years of Membership derive from
 * approvedAt, and Certificates is the count the completeness endpoint already returns. The other
 * four have no entity, table, or endpoint behind them and render as pending rather than as zeroes -
 * see StatTile's `pending` note.
 */
export const PsmpeJourneyCard = ({ member, completeness }: PsmpeJourneyCardProps) => {
  const joined = member.approvedAt ? new Date(member.approvedAt) : null
  // Whole years elapsed, floored - a member approved 11 months ago has completed 0 years, not 1.
  const yearsOfMembership =
    joined === null ? null : Math.max(0, Math.floor((Date.now() - joined.getTime()) / (365.25 * 24 * 60 * 60 * 1000)))

  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">My PSMPE Journey</h6>
      </div>
      <div className="card-body flex flex-col gap-3">
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-7 gap-2">
          <StatTile
            icon={LuCalendarDays}
            label="Member Since"
            value={joined ? joined.getFullYear() : null}
            sublabel={joined ? joined.toLocaleDateString() : undefined}
            pending={joined === null}
            accent="bg-primary/10 text-primary"
          />
          <StatTile
            icon={LuUsers}
            label="Years of Membership"
            value={yearsOfMembership}
            pending={yearsOfMembership === null}
            accent="bg-teal/15 text-teal"
          />
          <StatTile
            icon={LuFileBadge}
            label="Certificates"
            value={completeness?.certificateCount ?? null}
            pending={completeness === null}
            accent="bg-copper/15 text-copper"
          />
          <StatTile icon={LuPresentation} label="Conventions Attended" pending />
          <StatTile icon={LuPresentation} label="Technoforums Attended" pending />
          <StatTile icon={LuHandHeart} label="Volunteer Activities" pending />
          <StatTile icon={LuTrophy} label="Awards Received" pending />
        </div>

        <p className="text-xs text-default-500 rounded-lg bg-default-100 px-3 py-2 inline-flex items-center gap-2">
          <LuAward className="size-3.5 shrink-0" />
          Conventions, technoforums, volunteer work and awards aren't tracked by the portal yet.
        </p>
      </div>
    </div>
  )
}
