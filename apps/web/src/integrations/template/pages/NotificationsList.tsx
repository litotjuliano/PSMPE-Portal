import { Link } from 'react-router-dom'
import { LuBadgeCheck, LuUserPlus } from 'react-icons/lu'
import type { Member } from '../../../core/types/member'

interface NotificationsListProps {
  pendingApplications: Member[]
  pendingPrcVerifications: Member[]
}

export const NotificationsList = ({ pendingApplications, pendingPrcVerifications }: NotificationsListProps) => {
  const isEmpty = pendingApplications.length === 0 && pendingPrcVerifications.length === 0

  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">Notifications</h6>
      </div>
      <div className="flex flex-col divide-y divide-default-200">
        {pendingApplications.map((member) => (
          <Link key={member.id} to={`/members/${member.id}`} className="flex gap-3 p-4 items-start hover:bg-default-150">
            <div className="size-10 rounded-md bg-default-100 flex justify-center items-center shrink-0">
              <LuUserPlus className="size-5 text-primary" />
            </div>
            <div className="text-sm">
              <h6 className="font-medium text-default-800">
                New membership application: {member.firstName} {member.lastName}
              </h6>
              <p className="text-default-500">
                {member.chapter} - submitted {new Date(member.createdAt).toLocaleDateString()}
              </p>
            </div>
          </Link>
        ))}
        {pendingPrcVerifications.map((member) => (
          <Link key={member.id} to="/prc-verifications" className="flex gap-3 p-4 items-start hover:bg-default-150">
            <div className="size-10 rounded-md bg-default-100 flex justify-center items-center shrink-0">
              <LuBadgeCheck className="size-5 text-primary" />
            </div>
            <div className="text-sm">
              <h6 className="font-medium text-default-800">
                PRC License verification needed: {member.firstName} {member.lastName}
              </h6>
              <p className="text-default-500">{member.chapter}</p>
            </div>
          </Link>
        ))}
        {isEmpty && <p className="p-6 text-center text-sm text-default-500">No new notifications.</p>}
      </div>
    </div>
  )
}
