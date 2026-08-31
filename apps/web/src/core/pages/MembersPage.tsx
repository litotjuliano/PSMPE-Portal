import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { memberApi, type GetMembersParams } from '../api/endpoints/memberApi'
import { paymentApi, type Payment } from '../api/endpoints/paymentApi'
import type { Member, MembershipStatusValue } from '../types/member'
import { describeError } from '../utils/apiError'
import { useAuth } from '../auth/useAuth'
import { Roles } from '../types/auth'
import {
  ApproveApplicationWizard,
  MembersTable,
  PageBreadcrumb,
  PageMeta,
  PaymentsQueueTable,
  PaymentsSummaryPanel,
  type MembersView,
} from '../../integrations/template'

const PAGE_SIZE = 20

/**
 * The three member lists - all, pending membership approval, pending RMP verification - used to be
 * three nav entries, three pages and three tables. They are one query against `GET /api/members`
 * with different filters, so they are now one page with tabs.
 *
 * The two approvals stay separate *decisions*: membership approval admits an applicant once and
 * needs a Membership ID, while RMP verification recurs whenever licence details change and can be
 * rejected with a reason. A member can legitimately be waiting on both at the same time and will
 * appear in both tabs.
 */
/** The Payments tab lists payments rather than members, so it is not a MembersView - see the
 *  "one place for admin membership work" note in openspecs/payments.md. */
type TabKey = MembersView | 'payments'

const TABS: { key: TabKey; queueParam: string | null; label: string; filter: Partial<GetMembersParams> }[] = [
  { key: 'all', queueParam: null, label: 'All Members', filter: {} },
  { key: 'pendingApproval', queueParam: 'approval', label: 'Pending Approval', filter: { pendingApprovalOnly: true } },
  { key: 'pendingRmp', queueParam: 'rmp', label: 'RMP Verification', filter: { pendingPrcVerificationOnly: true } },
  { key: 'payments', queueParam: 'payments', label: 'Payments', filter: {} },
]

/** Default sort per tab - a work queue reads oldest-first, the full list reads alphabetically. */
const DEFAULT_SORT: Record<TabKey, { sortBy: NonNullable<GetMembersParams['sortBy']>; sortDir: 'asc' | 'desc' }> = {
  all: { sortBy: 'lastName', sortDir: 'asc' },
  pendingApproval: { sortBy: 'submittedAt', sortDir: 'asc' },
  pendingRmp: { sortBy: 'lastName', sortDir: 'asc' },
  // Unused - the payments queue orders oldest-first server-side and has no sortable headers.
  payments: { sortBy: 'lastName', sortDir: 'asc' },
}

export function MembersPage() {
  const { user } = useAuth()
  // False for an Approval user: they work the pendingApproval/pendingRmp queues below, but the
  // 'all' member list and the payments queue are read-only for them (Members.Manage-gated
  // server side, which they don't hold).
  const canManageMembers = user?.roles.includes(Roles.Admin) || user?.roles.includes(Roles.SuperAdmin) || false

  // The active tab lives in the URL so it is linkable - the redirects from the old
  // /membership-approvals and /prc-verifications routes and the notification bell all rely on it.
  const [searchParams, setSearchParams] = useSearchParams()
  const queueParam = searchParams.get('queue')
  const activeTab = TABS.find((t) => t.queueParam === queueParam) ?? TABS[0]
  const tabKey = activeTab.key
  const isPayments = tabKey === 'payments'

  const [members, setMembers] = useState<Member[]>([])
  const [payments, setPayments] = useState<Payment[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [sortBy, setSortBy] = useState<NonNullable<GetMembersParams['sortBy']>>(DEFAULT_SORT[tabKey].sortBy)
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>(DEFAULT_SORT[tabKey].sortDir)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [counts, setCounts] = useState({ pendingApproval: 0, pendingRmp: 0, payments: 0 })

  const [approving, setApproving] = useState<Member | null>(null)

  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<MembershipStatusValue | null>(null)

  // Debounces typing into the search box - fetchList only re-runs off `search`, not
  // `searchInput`, so a fetch fires once per pause in typing rather than per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchInput)
      setPage(1)
    }, 350)
    return () => clearTimeout(timer)
  }, [searchInput])

  /**
   * Applies the result unless `isStale()` says a newer request has superseded it. The staleness
   * check has to live here rather than only in the caller, because this is where the state is
   * written - a guard that only wrapped the caller's `.catch`/`.finally` would still let a late
   * response repaint the table.
   */
  const fetchList = useCallback(
    async (isStale: () => boolean = () => false) => {
      // The Payments tab is the one tab backed by a different entity and a different endpoint.
      if (isPayments) {
        const result = await paymentApi.getPayments({ page, pageSize: PAGE_SIZE, status: 'Submitted' })
        if (isStale()) return
        setPayments(result.items)
        setTotalCount(result.totalCount)
        return
      }

      const result = await memberApi.getMembers({
        page, pageSize: PAGE_SIZE, sortBy, sortDir, ...activeTab.filter,
        ...(tabKey === 'all' && search ? { search } : {}),
        ...(tabKey === 'all' && statusFilter !== null ? { status: statusFilter } : {}),
      })
      if (isStale()) return
      setMembers(result.items)
      setTotalCount(result.totalCount)
    },
    // activeTab is derived from the URL, so depending on the param itself keeps this stable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [page, sortBy, sortDir, queueParam, search, statusFilter],
  )

  /**
   * Tab badges. Two `pageSize: 1` calls read `totalCount` off a one-row response rather than
   * needing a dedicated counts endpoint - the topbar bell already fetches both queues the same
   * way. Refetched after every decision so the badges never lag the table.
   */
  const fetchCounts = useCallback(async () => {
    const [approval, rmp, pendingPayments] = await Promise.all([
      memberApi.getMembers({ page: 1, pageSize: 1, pendingApprovalOnly: true }),
      memberApi.getMembers({ page: 1, pageSize: 1, pendingPrcVerificationOnly: true }),
      paymentApi.getPayments({ page: 1, pageSize: 1, status: 'Submitted' }),
    ])
    setCounts({
      pendingApproval: approval.totalCount,
      pendingRmp: rmp.totalCount,
      payments: pendingPayments.totalCount,
    })
  }, [])

  useEffect(() => {
    // Guarded against out-of-order responses, same as the Users list: switching tabs quickly fires
    // overlapping requests, and the slower one landing last would repaint with the wrong tab's rows.
    let cancelled = false
    setLoading(true)
    setError(null)
    fetchList(() => cancelled)
      .catch((err) => {
        if (!cancelled) setError(describeError(err, 'Could not load this list. Please try again.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [fetchList])

  useEffect(() => {
    // A failed count only costs a badge, so it must never surface as a page-level error.
    fetchCounts().catch(() => {})
  }, [fetchCounts])

  const refresh = async () => {
    await Promise.all([fetchList(), fetchCounts().catch(() => {})])
  }

  const handleTabChange = (tab: (typeof TABS)[number]) => {
    if (tab.key === tabKey) return
    setPage(1)
    setSortBy(DEFAULT_SORT[tab.key].sortBy)
    setSortDir(DEFAULT_SORT[tab.key].sortDir)
    setSearchInput('')
    setSearch('')
    setStatusFilter(null)
    setSearchParams(tab.queueParam ? { queue: tab.queueParam } : {}, { replace: true })
  }

  const handleSortChange = (column: NonNullable<GetMembersParams['sortBy']>) => {
    if (column === sortBy) {
      setSortDir((current) => (current === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortBy(column)
      setSortDir('asc')
    }
    setPage(1)
  }

  const handleStatusFilterChange = (status: MembershipStatusValue | null) => {
    setStatusFilter(status)
    setPage(1)
  }

  const handleDelete = (id: string) => {
    memberApi.deleteMember(id).then(refresh)
  }

  const handleVerifyRmp = (id: string) => {
    memberApi.approvePrcVerification(id).then(refresh)
  }

  const handleRejectRmp = (id: string, reason: string) => {
    memberApi.rejectPrcVerification(id, reason).then(refresh)
  }

  const handleVerifyPayment = (id: string) => {
    paymentApi.verifyPayment(id).then(refresh)
  }

  const handleRejectPayment = (id: string, reason: string) => {
    paymentApi.rejectPayment(id, reason).then(refresh)
  }

  const countFor = (key: TabKey) =>
    key === 'pendingApproval' ? counts.pendingApproval
      : key === 'pendingRmp' ? counts.pendingRmp
      : key === 'payments' ? counts.payments
      : null

  return (
    <>
      <PageMeta title="Members" />
      <main>
        <PageBreadcrumb title="Members" />

        {/* Scrolls rather than wrapping on narrow screens, so the strip never pushes the card wide. */}
        <div className="flex items-center gap-2 mb-4 overflow-x-auto" role="tablist" aria-label="Member lists">
          {TABS.map((tab) => {
            const isActive = tab.key === tabKey
            const count = countFor(tab.key)
            return (
              <button
                key={tab.label}
                type="button"
                role="tab"
                aria-selected={isActive}
                onClick={() => handleTabChange(tab)}
                className={`btn btn-sm whitespace-nowrap inline-flex items-center gap-2 ${
                  isActive ? 'bg-primary text-white' : 'border border-default-200 text-default-700'
                }`}
              >
                {tab.label}
                {count !== null && count > 0 && (
                  <span
                    className={`inline-flex items-center justify-center min-w-5 px-1.5 rounded-full text-xs font-semibold ${
                      isActive ? 'bg-white/20 text-white' : 'bg-primary/10 text-primary'
                    }`}
                  >
                    {count}
                  </span>
                )}
              </button>
            )
          })}
        </div>

        {error && <p className="text-sm text-danger mb-4">{error}</p>}

        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : isPayments ? (
          <>
            <PaymentsSummaryPanel />
            <PaymentsQueueTable
              payments={payments}
              canManagePayments={canManageMembers}
              onVerify={handleVerifyPayment}
              onReject={handleRejectPayment}
              page={page}
              pageSize={PAGE_SIZE}
              totalCount={totalCount}
              onPageChange={setPage}
            />
          </>
        ) : (
          <MembersTable
            members={members}
            view={tabKey}
            canManageMembers={canManageMembers}
            searchInput={searchInput}
            onSearchInputChange={setSearchInput}
            statusFilter={statusFilter}
            onStatusFilterChange={handleStatusFilterChange}
            sortBy={sortBy}
            sortDir={sortDir}
            onSortChange={handleSortChange}
            page={page}
            pageSize={PAGE_SIZE}
            totalCount={totalCount}
            onPageChange={setPage}
            onDelete={handleDelete}
            onApprove={setApproving}
            onVerifyRmp={handleVerifyRmp}
            onRejectRmp={handleRejectRmp}
          />
        )}

        <ApproveApplicationWizard member={approving} onApproved={refresh} onCancel={() => setApproving(null)} />
      </main>
    </>
  )
}
