import type { UserSummary } from '../../../core/api/endpoints/adminApi'

interface AdminUsersTableProps {
  users: UserSummary[]
}

function initialsOf(name: string) {
  return name
    .split(' ')
    .map((part) => part.charAt(0))
    .join('')
    .slice(0, 2)
    .toUpperCase()
}

export const AdminUsersTable = ({ users }: AdminUsersTableProps) => {
  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">Users</h6>
      </div>

      <div className="flex flex-col">
        <div className="overflow-x-auto">
          <div className="min-w-full inline-block align-middle">
            <div className="overflow-hidden">
              <table className="min-w-full divide-y divide-default-200">
                <thead className="bg-default-150">
                  <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
                    <th className="px-3.5 py-3 text-start">Name</th>
                    <th className="px-3.5 py-3 text-start">Email</th>
                    <th className="px-3.5 py-3 text-start">Roles</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {users.map((user) => (
                    <tr key={user.id} className="text-default-800 font-normal text-sm whitespace-nowrap">
                      <td className="flex py-3 px-3.5 items-center gap-3">
                        <div className="w-9 h-9 flex items-center justify-center rounded-full bg-primary/10 text-primary font-semibold text-xs">
                          {initialsOf(user.displayName)}
                        </div>
                        <span className="font-semibold">{user.displayName}</span>
                      </td>
                      <td className="py-3 px-3.5">{user.email}</td>
                      <td className="py-3 px-3.5">
                        <div className="flex flex-wrap gap-1.5">
                          {user.roles.map((role) => (
                            <span key={role} className="py-0.5 px-2.5 inline-flex items-center text-xs font-medium bg-default-150 text-default-600 rounded">
                              {role}
                            </span>
                          ))}
                        </div>
                      </td>
                    </tr>
                  ))}
                  {users.length === 0 && (
                    <tr>
                      <td colSpan={3} className="py-6 px-3.5 text-center text-default-500">
                        No users yet.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
      {/* TODO: add role-assignment UI (Super Admin only) once needed beyond this starter. */}
    </div>
  )
}
