import { useEffect, useState } from 'react'
import { adminApi, type GetUsersParams, type UserSummary } from '../api/endpoints/adminApi'
import { AdminUsersTable, PageBreadcrumb, PageMeta } from '../../integrations/template'
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

  const canManageRoles = user?.roles.includes(Roles.SuperAdmin) ?? false

  const refetch = () =>
    adminApi.getUsers({ page, pageSize: PAGE_SIZE, sortBy, sortDir }).then((result) => {
      setUsers(result.items)
      setTotalCount(result.totalCount)
    })

  useEffect(() => {
    setLoading(true)
    refetch().finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, sortBy, sortDir])

  const handleToggleRole = (userId: string, role: Role, hasRole: boolean) => {
    const request = hasRole ? adminApi.removeRole(userId, role) : adminApi.assignRole(userId, role)
    request.then(refetch)
  }

  const handleDelete = (id: string) => {
    adminApi.deleteUser(id).then(refetch)
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

  return (
    <>
      <PageMeta title="Users" />
      <main>
        <PageBreadcrumb title="Users" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <AdminUsersTable
            users={users}
            canManageRoles={canManageRoles}
            onToggleRole={handleToggleRole}
            onDelete={handleDelete}
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
