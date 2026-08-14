import { Link } from 'react-router-dom'
import { LuCalendarClock, LuClipboardCheck, LuShieldCheck } from 'react-icons/lu'
import { StatTile } from '../shared/StatTile'
import type { MemberStats } from '../../../../core/api/endpoints/memberApi'

type ActionItems = MemberStats['actionItems']

/**
 * Three work-queue counts, each linking into the Members page. Pending Approvals and Pending PRC
 * Verification map onto MembersPage's existing `?queue=approval` / `?queue=rmp` tabs - the same
 * deep links already used by the topbar bell, NotificationsList, and the old
 * /membership-approvals /prc-verifications route redirects (see router.tsx). Renewals Due Soon has
 * no matching tab/filter on MembersPage, so it links to the plain member list rather than inventing
 * new query-param filtering there.
 */
export function ActionItemsWidget({ actionItems }: { actionItems: ActionItems }) {
  return (
    <div className="card h-full">
      <div className="card-header">
        <h6 className="card-title">Action Items</h6>
      </div>
      <div className="card-body">
        <div className="grid grid-cols-1 sm:grid-cols-3 lg:grid-cols-1 gap-2">
          <Link to="/members?queue=approval" className="block rounded-md hover:bg-default-100 transition-colors">
            <StatTile
              icon={LuClipboardCheck}
              label="Pending Approvals"
              value={actionItems.pendingApprovals}
              accent="bg-warning/15 text-warning"
            />
          </Link>
          <Link to="/members?queue=rmp" className="block rounded-md hover:bg-default-100 transition-colors">
            <StatTile
              icon={LuShieldCheck}
              label="Pending PRC Verification"
              value={actionItems.pendingPrcVerification}
              accent="bg-info/15 text-info"
            />
          </Link>
          <Link to="/members" className="block rounded-md hover:bg-default-100 transition-colors">
            <StatTile
              icon={LuCalendarClock}
              label="Renewals Due Soon"
              value={actionItems.renewalsDueSoon}
              accent="bg-danger/15 text-danger"
            />
          </Link>
        </div>
      </div>
    </div>
  )
}
