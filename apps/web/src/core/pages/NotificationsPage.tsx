import { useEffect, useState } from 'react'
import { memberApi } from '../api/endpoints/memberApi'
import type { Member } from '../types/member'
import { NotificationsList, PageBreadcrumb, PageMeta } from '../../integrations/template'

export function NotificationsPage() {
  const [pendingApplications, setPendingApplications] = useState<Member[]>([])
  const [pendingPrcVerifications, setPendingPrcVerifications] = useState<Member[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([
      memberApi.getMembers({ pendingApprovalOnly: true, pageSize: 50, sortBy: 'membershipNo' }),
      memberApi.getMembers({ pendingPrcVerificationOnly: true, pageSize: 50, sortBy: 'membershipNo' }),
    ])
      .then(([applications, prcVerifications]) => {
        setPendingApplications(applications.items)
        setPendingPrcVerifications(prcVerifications.items)
      })
      .finally(() => setLoading(false))
  }, [])

  return (
    <>
      <PageMeta title="Notifications" />
      <main>
        <PageBreadcrumb title="Notifications" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <NotificationsList pendingApplications={pendingApplications} pendingPrcVerifications={pendingPrcVerifications} />
        )}
      </main>
    </>
  )
}
