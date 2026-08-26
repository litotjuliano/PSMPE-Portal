import { useEffect, useState } from 'react'
import { memberApi, type MemberStats } from '../../../../core/api/endpoints/memberApi'
import { describeError } from '../../../../core/utils/apiError'
import { ActionItemsWidget } from './ActionItemsWidget'
import { MembershipBreakdownCharts } from './MembershipBreakdownCharts'
import { MembershipStatusBreakdown } from './MembershipStatusBreakdown'
import { MembershipWelcomeBanner } from './MembershipWelcomeBanner'
import { RegistrationTrendChart } from './RegistrationTrendChart'

/**
 * Real Membership statistics dashboard, replacing the fully fake e-commerce dashboard shown to
 * Admin/staff users - see GET /api/members/stats (Members.View-gated). Wired into DashboardPage,
 * gated on `!isMember` there; the old fake dashboard/ folder it replaced has been deleted.
 */
export function MembershipDashboard() {
  const [stats, setStats] = useState<MemberStats | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    memberApi
      .getStats()
      .then((result) => {
        if (!cancelled) setStats(result)
      })
      .catch((err) => {
        if (!cancelled) setError(describeError(err, 'Could not load membership statistics. Please try again.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <>
      <MembershipWelcomeBanner />

      {error && <p className="text-sm text-danger mb-4">{error}</p>}

      {loading ? (
        <p className="text-sm text-default-500">Loading membership statistics…</p>
      ) : stats ? (
        <div className="flex flex-col gap-5">
          <div className="grid lg:grid-cols-3 grid-cols-1 gap-5">
            <div className="lg:col-span-2 col-span-1">
              <MembershipStatusBreakdown statusCounts={stats.statusCounts} />
            </div>
            <ActionItemsWidget actionItems={stats.actionItems} />
          </div>
          <RegistrationTrendChart registrationTrend={stats.registrationTrend} />
          <MembershipBreakdownCharts byChapter={stats.byChapter} byMemberType={stats.byMemberType} />
        </div>
      ) : null}
    </>
  )
}
