import { useEffect, useState } from 'react'
import { isAxiosError } from 'axios'
import { adminApi, type GetUsersParams, type UserSummary } from '../api/endpoints/adminApi'
import { AdminUsersTable, PageBreadcrumb, PageMeta, Toast, type ToastStatus } from '../../integrations/template'
import { useAuth } from '../auth/useAuth'
import { Roles, type Role } from '../types/auth'

const PAGE_SIZE = 20

export function AdminUsersPage() {
  const { user } = useAuth()
  const [users, setUsers] = useState<UserSummary[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [sortBy, setSortBy] = useState<NonNullable<GetUsersParams['sortBy']>>('displayName')
  const [sortDir, setSortDir] = useState<NonNullable<GetUsersParams['sortDir']>>('asc')
  const [loading, setLoading] = useState(true)
  // Result of the last send-email action (password reset, resend, bulk resend), shown as a toast.
  const [resetStatus, setResetStatus] = useState<ToastStatus | null>(null)
  const [bulkResending, setBulkResending] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)

  // Gates role-checkbox editing (unchanged) and, as of this change, the per-row Edit/Delete
  // icons too - both hidden entirely for a regular Admin, leaving only Email Verification.
  const isSuperAdmin = user?.roles.includes(Roles.SuperAdmin) ?? false
  // Approximates the server's admin:manage-users permission / RequireAdmin policy, which the
  // token doesn't carry - it ships with both roles by default, and the API enforces the real
  // check. False for Approval (view-only here).
  const canManageUsers = isSuperAdmin || (user?.roles.includes(Roles.Admin) ?? false)

  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [roleFilter, setRoleFilter] = useState<Role[]>([])

  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchInput)
      setPage(1)
    }, 350)
    return () => clearTimeout(timer)
  }, [searchInput])

  const fetchUsers = () =>
    adminApi.getUsers({
      page,
      pageSize: PAGE_SIZE,
      sortBy,
      sortDir,
      ...(search ? { search } : {}),
      ...(roleFilter.length > 0 ? { roles: roleFilter } : {}),
    })

  /** Refetch for the current filters, used after a mutation. */
  const refetch = () =>
    fetchUsers().then((result) => {
      setUsers(result.items)
      setTotalCount(result.totalCount)
    })

  useEffect(() => {
    // Guarded against out-of-order responses: toggling two role chips quickly fires two requests,
    // and without this the slower first one can land last and repaint the list with results for a
    // filter that is no longer selected - which reads as "the filter is wrong".
    let cancelled = false
    setLoading(true)
    setLoadError(null)
    fetchUsers()
      .then((result) => {
        if (cancelled) return
        setUsers(result.items)
        setTotalCount(result.totalCount)
      })
      .catch((err) => {
        if (cancelled) return
        // Without this a failed request left the empty list in place, which reads as "No users
        // yet." - a wrong answer that hides the real cause (expired session, 403, server down).
        const status = isAxiosError(err) ? err.response?.status : undefined
        setLoadError(
          status === 403
            ? "You don't have permission to view users."
            : status
              ? `Could not load users (error ${status}). Try reloading the page.`
              : 'Could not reach the server. Check your connection and try again.',
        )
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, sortBy, sortDir, search, roleFilter])

  const handleToggleRole = (userId: string, role: Role, hasRole: boolean) => {
    const request = hasRole ? adminApi.removeRole(userId, role) : adminApi.assignRole(userId, role)
    request.then(refetch)
  }

  const handleDelete = (id: string) => {
    adminApi.deleteUser(id).then(refetch)
  }

  const handleVerifyEmail = (userId: string) => {
    adminApi.verifyEmail(userId).then(refetch)
  }

  const handleSendPasswordReset = (userId: string) => {
    // No refetch: sending a reset changes nothing on this list. The account keeps its current
    // password until the member actually uses the emailed link.
    setResetStatus(null)
    adminApi
      .sendPasswordReset(userId)
      .then(() => setResetStatus({ ok: true, text: 'Password reset email sent.' }))
      .catch((err) =>
        setResetStatus({
          ok: false,
          text:
            (isAxiosError(err) && (err.response?.data as { message?: string } | undefined)?.message) ||
            'Could not send the password reset email. Please try again.',
        }),
      )
  }

  const handleResendVerificationToAll = () => {
    setBulkResending(true)
    setResetStatus(null)
    adminApi
      .resendVerificationToAllUnverified()
      .then((result) => {
        const summary = `Sent ${result.sent} of ${result.total} verification emails${result.failed > 0 ? `, ${result.failed} failed` : ''}.`
        if (result.stoppedEarly) {
          setResetStatus({ ok: false, text: `${summary} Stopped early because the email service looks to be down - try again in a few minutes.` })
        } else if (result.total > result.attempted) {
          setResetStatus({ ok: false, text: `${summary} ${result.total - result.attempted} more are still unverified - click again to continue.` })
        } else {
          setResetStatus({ ok: result.failed === 0, text: summary })
        }
        return refetch().catch(() => undefined)
      })
      .catch((err) =>
        setResetStatus({
          ok: false,
          text:
            (isAxiosError(err) && (err.response?.data as { message?: string } | undefined)?.message) ||
            'Could not send the verification emails. Please try again.',
        }),
      )
      .finally(() => setBulkResending(false))
  }

  const handleResendVerificationEmail = (userId: string) => {
    setResetStatus(null)
    adminApi
      .resendVerificationEmail(userId)
      // Refetch so the row picks up its "sent" mark straight away.
      .then(() => {
        setResetStatus({ ok: true, text: 'Verification email sent.' })
        // A failed refresh must not read as a failed send.
        return refetch().catch(() => undefined)
      })
      .catch((err) =>
        setResetStatus({
          ok: false,
          text:
            (isAxiosError(err) && (err.response?.data as { message?: string } | undefined)?.message) ||
            'Could not send the verification email. Please try again.',
        }),
      )
  }

  const handleSortChange = (column: NonNullable<GetUsersParams['sortBy']>) => {
    if (column === sortBy) {
      setSortDir((current) => (current === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortBy(column)
      setSortDir('asc')
    }
    setPage(1)
  }

  const handleRoleFilterToggle = (role: Role) => {
    setRoleFilter((current) => (current.includes(role) ? current.filter((r) => r !== role) : [...current, role]))
    setPage(1)
  }

  return (
    <>
      <PageMeta title="Users" />
      <main>
        <PageBreadcrumb title="Users" />
        <Toast status={resetStatus} onDismiss={() => setResetStatus(null)} />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : loadError ? (
          <p className="text-sm text-danger">{loadError}</p>
        ) : (
          <AdminUsersTable
            users={users}
            canManageRoles={isSuperAdmin}
            isSuperAdmin={isSuperAdmin}
            canManageUsers={canManageUsers}
            searchInput={searchInput}
            onSearchInputChange={setSearchInput}
            roleFilter={roleFilter}
            onRoleFilterToggle={handleRoleFilterToggle}
            canSendPasswordReset={canManageUsers}
            onSendPasswordReset={handleSendPasswordReset}
            onResendVerificationEmail={handleResendVerificationEmail}
            onResendVerificationToAll={handleResendVerificationToAll}
            bulkResending={bulkResending}
            onToggleRole={handleToggleRole}
            onDelete={handleDelete}
            onVerifyEmail={handleVerifyEmail}
            currentUserEmail={user?.email}
            sortBy={sortBy}
            sortDir={sortDir}
            onSortChange={handleSortChange}
            page={page}
            pageSize={PAGE_SIZE}
            totalCount={totalCount}
            onPageChange={setPage}
          />
        )}
      </main>
    </>
  )
}
