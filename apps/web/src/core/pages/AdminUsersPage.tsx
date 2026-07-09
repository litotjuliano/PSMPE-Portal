import { useEffect, useState } from 'react'
import { adminApi, type UserSummary } from '../api/endpoints/adminApi'

export function AdminUsersPage() {
  const [users, setUsers] = useState<UserSummary[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    adminApi
      .getUsers()
      .then(setUsers)
      .finally(() => setLoading(false))
  }, [])

  if (loading) {
    return <p className="text-sm text-gray-500">Loading…</p>
  }

  return (
    <div>
      <h1 className="mb-4 text-2xl font-semibold text-gray-900">Users</h1>
      <table className="w-full overflow-hidden rounded-md border border-gray-200 bg-white text-left text-sm">
        <thead className="bg-gray-50 text-xs uppercase text-gray-500">
          <tr>
            <th className="px-4 py-2">Name</th>
            <th className="px-4 py-2">Email</th>
            <th className="px-4 py-2">Roles</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-200">
          {users.map((u) => (
            <tr key={u.id}>
              <td className="px-4 py-2">{u.displayName}</td>
              <td className="px-4 py-2">{u.email}</td>
              <td className="px-4 py-2">{u.roles.join(', ')}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {/* TODO: add role-assignment UI (Super Admin only) once needed beyond this starter. */}
    </div>
  )
}
