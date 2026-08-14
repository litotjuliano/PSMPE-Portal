import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { memberApi, type MemberStats } from '../../../../core/api/endpoints/memberApi'
import { useAuth } from '../../../../core/auth/useAuth'
import { describeError } from '../../../../core/utils/apiError'
import { ActionItemsWidget } from './ActionItemsWidget'
import { MembershipBreakdownCharts } from './MembershipBreakdownCharts'
import { MembershipStatusBreakdown } from './MembershipStatusBreakdown'
import { RegistrationTrendChart } from './RegistrationTrendChart'

/**
 * Trimmed, membership-flavored stand-in for the template's WelcomeUser
 * (components/dashboard/WelcomeUser.tsx) - same greeting and decorative background art, but the
 * CTA now points at /members (the primary admin action here) instead of the CMS-specific
 * /content/new. WelcomeUser itself is left untouched - a later task deletes the whole fake
 * dashboard/ folder wholesale, this just doesn't import from it.
 */
function MembershipWelcomeBanner() {
  const { user } = useAuth()

  return (
    <div className="card-body relative overflow-hidden bg-zinc-900 rounded-md mb-5">
      <div className="relative z-10">
        <h5 className="mb-3 text-lg text-white">Welcome {user?.displayName} 🎉</h5>
        <p className="mb-5 text-white/70 text-sm max-w-lg">
          Here's how PSMPE membership is trending - status, registrations, and what needs your attention.
        </p>
        <Link to="/members" className="btn bg-primary text-white">
          View Members
        </Link>
      </div>

      <div className="absolute inset-0">
        <svg
          xmlns="http://www.w3.org/2000/svg"
          className="size-full"
          version="1.1"
          xmlnsXlink="http://www.w3.org/1999/xlink"
          preserveAspectRatio="none"
          viewBox="0 0 1440 560"
        >
          <g mask='url("#SvgjsMask1000")' fill="none">
            <use xlinkHref="#SvgjsSymbol1007" x="0" y="0"></use>
            <use xlinkHref="#SvgjsSymbol1007" x="720" y="0"></use>
          </g>
          <defs>
            <mask id="SvgjsMask1000">
              <rect width="1440" height="560" fill="#ffffff"></rect>
            </mask>
            <path d="M-1 0 a1 1 0 1 0 2 0 a1 1 0 1 0 -2 0z" id="SvgjsPath1003"></path>
            <path d="M-3 0 a3 3 0 1 0 6 0 a3 3 0 1 0 -6 0z" id="SvgjsPath1004"></path>
            <path d="M-5 0 a5 5 0 1 0 10 0 a5 5 0 1 0 -10 0z" id="SvgjsPath1001"></path>
            <path d="M2 -2 L-2 2z" id="SvgjsPath1005"></path>
            <path d="M6 -6 L-6 6z" id="SvgjsPath1002"></path>
            <path d="M30 -30 L-30 30z" id="SvgjsPath1006"></path>
          </defs>
          <symbol id="SvgjsSymbol1007">
            <use xlinkHref="#SvgjsPath1001" x="30" y="30" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="30" y="90" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="30" y="150" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1003" x="30" y="210" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="30" y="270" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="30" y="330" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="30" y="390" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1003" x="30" y="450" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="30" y="510" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="30" y="570" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="90" y="30" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1003" x="90" y="90" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="90" y="150" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="90" y="210" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1004" x="90" y="270" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1003" x="90" y="330" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="90" y="390" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="90" y="450" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="90" y="510" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="90" y="570" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="150" y="30" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="150" y="90" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="150" y="150" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="150" y="210" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="150" y="270" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1006" x="150" y="330" stroke="rgba(32, 43, 61, 1)" strokeWidth="3"></use>
            <use xlinkHref="#SvgjsPath1004" x="150" y="390" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="150" y="450" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="150" y="510" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="150" y="570" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="210" y="30" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="210" y="90" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1006" x="210" y="150" stroke="rgba(32, 43, 61, 1)" strokeWidth="3"></use>
            <use xlinkHref="#SvgjsPath1002" x="210" y="210" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="210" y="270" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="210" y="330" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="210" y="390" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="210" y="450" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1006" x="210" y="510" stroke="rgba(32, 43, 61, 1)" strokeWidth="3"></use>
            <use xlinkHref="#SvgjsPath1003" x="210" y="570" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="270" y="30" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="270" y="90" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="270" y="150" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="270" y="210" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="270" y="270" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="270" y="330" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1006" x="270" y="390" stroke="rgba(32, 43, 61, 1)" strokeWidth="3"></use>
            <use xlinkHref="#SvgjsPath1002" x="270" y="450" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="270" y="510" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="270" y="570" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="330" y="30" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1006" x="330" y="90" stroke="rgba(32, 43, 61, 1)" strokeWidth="3"></use>
            <use xlinkHref="#SvgjsPath1002" x="330" y="150" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="330" y="210" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1006" x="330" y="270" stroke="rgba(32, 43, 61, 1)" strokeWidth="3"></use>
            <use xlinkHref="#SvgjsPath1001" x="330" y="330" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="330" y="390" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="330" y="450" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1003" x="330" y="510" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="330" y="570" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1004" x="390" y="30" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="390" y="90" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="390" y="150" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="390" y="210" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="390" y="270" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="390" y="330" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="390" y="390" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1003" x="390" y="450" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="390" y="510" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="390" y="570" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="450" y="30" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1004" x="450" y="90" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="450" y="150" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="450" y="210" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="450" y="270" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="450" y="330" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="450" y="390" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="450" y="450" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="450" y="510" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="450" y="570" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="510" y="30" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1003" x="510" y="90" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="510" y="150" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="510" y="210" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="510" y="270" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1004" x="510" y="330" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1006" x="510" y="390" stroke="rgba(32, 43, 61, 1)" strokeWidth="3"></use>
            <use xlinkHref="#SvgjsPath1001" x="510" y="450" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="510" y="510" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="510" y="570" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="570" y="30" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="570" y="90" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="570" y="150" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="570" y="210" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="570" y="270" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="570" y="330" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1006" x="570" y="390" stroke="rgba(32, 43, 61, 1)" strokeWidth="3"></use>
            <use xlinkHref="#SvgjsPath1005" x="570" y="450" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="570" y="510" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="570" y="570" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="630" y="30" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="630" y="90" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="630" y="150" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="630" y="210" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="630" y="270" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1006" x="630" y="330" stroke="rgba(32, 43, 61, 1)" strokeWidth="3"></use>
            <use xlinkHref="#SvgjsPath1002" x="630" y="390" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1006" x="630" y="450" stroke="rgba(32, 43, 61, 1)" strokeWidth="3"></use>
            <use xlinkHref="#SvgjsPath1001" x="630" y="510" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="630" y="570" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="690" y="30" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="690" y="90" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="690" y="150" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1002" x="690" y="210" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1005" x="690" y="270" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1001" x="690" y="330" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1003" x="690" y="390" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1003" x="690" y="450" stroke="rgba(32, 43, 61, 1)"></use>
            <use xlinkHref="#SvgjsPath1006" x="690" y="510" stroke="rgba(32, 43, 61, 1)" strokeWidth="3"></use>
            <use xlinkHref="#SvgjsPath1003" x="690" y="570" stroke="rgba(32, 43, 61, 1)"></use>
          </symbol>
        </svg>
      </div>
    </div>
  )
}

/**
 * Real Membership statistics dashboard, replacing the fully fake e-commerce dashboard shown to
 * Admin/staff users - see GET /api/members/stats (Members.View-gated). Not yet wired into
 * DashboardPage - a later task swaps it in and deletes the old dashboard/ folder.
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
