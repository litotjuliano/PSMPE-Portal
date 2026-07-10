import { useEffect, useState } from 'react'
import { adminApi, type UserSummary } from '../api/endpoints/adminApi'
import { AdminUsersTable, PageBreadcrumb, PageMeta } from '../../integrations/template'

export function AdminUsersPage() {
  const [users, setUsers] = useState<UserSummary[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    adminApi
      .getUsers()
      .then(setUsers)
      .finally(() => setLoading(false))
  }, [])

  return (
    <>
      <PageMeta title="Users" />
      <main>
        <PageBreadcrumb title="Users" />
        {loading ? <p className="text-sm text-default-500">Loading…</p> : <AdminUsersTable users={users} />}
      </main>
    </>
  )
}
