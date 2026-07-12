import { useState } from 'react'
import { Link } from 'react-router-dom'
import { LuChevronDown, LuChevronUp, LuPlus, LuSquarePen, LuTrash2 } from 'react-icons/lu'
import type { GetUsersParams, UserSummary } from '../../../core/api/endpoints/adminApi'
import { AssignableRoles, Roles, type Role } from '../../../core/types/auth'
import { ConfirmationModal } from '../components/shared/ConfirmationModal'

type SortableColumn = NonNullable<GetUsersParams['sortBy']>

interface AdminUsersTableProps {
  users: UserSummary[]
  canManageRoles: boolean
  onToggleRole: (userId: string, role: Role, hasRole: boolean) => void
  onDelete: (id: string) => void
  currentUserEmail?: string
  sortBy: SortableColumn
  sortDir: 'asc' | 'desc'
  onSortChange: (column: SortableColumn) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

function initialsOf(name: string) {
  return name
    .split(' ')
    .map((part) => part.charAt(0))
    .join('')
    .slice(0, 2)
    .toUpperCase()
}

function SortableHeader({
  column,
  label,
  sortBy,
  sortDir,
  onSortChange,
}: {
  column: SortableColumn
  label: string
  sortBy: SortableColumn
  sortDir: 'asc' | 'desc'
  onSortChange: (column: SortableColumn) => void
}) {
  const isActive = sortBy === column
  return (
    <th className="px-3.5 py-3 text-start">
      <button type="button" onClick={() => onSortChange(column)} className="inline-flex items-center gap-1 hover:text-default-900">
        {label}
        {isActive && (sortDir === 'asc' ? <LuChevronUp className="size-3.5" /> : <LuChevronDown className="size-3.5" />)}
      </button>
    </th>
  )
}

export const AdminUsersTable = ({
  users,
  canManageRoles,
  onToggleRole,
  onDelete,
  currentUserEmail,
  sortBy,
  sortDir,
  onSortChange,
  page,
  pageSize,
  totalCount,
  onPageChange,
}: AdminUsersTableProps) => {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const [deletingUser, setDeletingUser] = useState<UserSummary | null>(null)

  return (
    <div className="card">
      <div className="card-header flex justify-between items-center">
        <h6 className="card-title">Users</h6>
        <Link to="/admin/users/new" className="btn btn-sm bg-primary text-white">
          <LuPlus className="size-4 me-1" />
          New user
        </Link>
      </div>

      <div className="flex flex-col">
        <div className="overflow-x-auto">
          <div className="min-w-full inline-block align-middle">
            <div className="overflow-hidden">
              <table className="min-w-full divide-y divide-default-200">
                <thead className="bg-default-150">
                  <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
                    <SortableHeader column="displayName" label="Name" sortBy={sortBy} sortDir={sortDir} onSortChange={onSortChange} />
                    <SortableHeader column="email" label="Email" sortBy={sortBy} sortDir={sortDir} onSortChange={onSortChange} />
                    <th className="px-3.5 py-3 text-start">Roles</th>
                    <SortableHeader column="createdAt" label="Joined" sortBy={sortBy} sortDir={sortDir} onSortChange={onSortChange} />
                    <th className="px-3.5 py-3 text-start">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {users.map((user) => {
                    // A Super Admin row is only ever the caller's own (backend hides every other
                    // Super Admin's row) - fully read-only, changes only happen via seeding/config/
                    // direct DB, never through this screen.
                    const isSuperAdminRow = user.roles.includes(Roles.SuperAdmin)
                    return (
                    <tr key={user.id} className="text-default-800 font-normal text-sm whitespace-nowrap">
                      <td className="flex py-3 px-3.5 items-center gap-3">
                        <div className="w-9 h-9 flex items-center justify-center rounded-full bg-primary/10 text-primary font-semibold text-xs">
                          {initialsOf(user.displayName)}
                        </div>
                        <span className="font-semibold">{user.displayName}</span>
                      </td>
                      <td className="py-3 px-3.5">{user.email}</td>
                      <td className="py-3 px-3.5">
                        <div className="flex flex-wrap gap-2.5">
                          {AssignableRoles.map((role) => {
                            const hasRole = user.roles.includes(role)
                            const disabled = !canManageRoles || isSuperAdminRow
                            return (
                              <label
                                key={role}
                                className={`py-0.5 px-2.5 inline-flex items-center gap-1.5 text-xs font-medium rounded ${
                                  hasRole ? 'bg-primary/10 text-primary' : 'bg-default-150 text-default-600'
                                } ${disabled ? 'cursor-default' : 'cursor-pointer'}`}
                              >
                                <input
                                  type="checkbox"
                                  className="form-checkbox size-3.5"
                                  checked={hasRole}
                                  disabled={disabled}
                                  onChange={() => onToggleRole(user.id, role, hasRole)}
                                />
                                {role}
                              </label>
                            )
                          })}
                        </div>
                      </td>
                      <td className="py-3 px-3.5 text-default-500">{new Date(user.createdAt).toLocaleDateString()}</td>
                      <td className="py-3 px-3.5">
                        <div className="flex items-center gap-1.5">
                          {isSuperAdminRow ? (
                            <span
                              className="btn btn-icon size-8 rounded-full text-default-300 cursor-not-allowed"
                              aria-label="Edit disabled - Super Admin accounts are read-only"
                            >
                              <LuSquarePen className="size-4" />
                            </span>
                          ) : (
                            <Link
                              to={`/admin/users/${user.id}`}
                              className="btn btn-icon size-8 hover:bg-default-150 rounded-full text-default-500"
                              aria-label="Edit"
                            >
                              <LuSquarePen className="size-4" />
                            </Link>
                          )}
                          {user.email !== currentUserEmail && (
                            <button
                              onClick={() => setDeletingUser(user)}
                              className="btn btn-icon size-8 hover:bg-danger/10 hover:text-danger rounded-full text-default-500"
                              aria-label="Delete"
                            >
                              <LuTrash2 className="size-4" />
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                    )
                  })}
                  {users.length === 0 && (
                    <tr>
                      <td colSpan={5} className="py-6 px-3.5 text-center text-default-500">
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

      <div className="card-footer flex items-center justify-between">
        <span className="text-sm text-default-500">
          Page {page} of {totalPages} ({totalCount} total)
        </span>
        <div className="flex items-center gap-1.5">
          <button
            type="button"
            className="btn btn-sm border border-default-200 disabled:opacity-50"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
          >
            Previous
          </button>
          <button
            type="button"
            className="btn btn-sm border border-default-200 disabled:opacity-50"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
          >
            Next
          </button>
        </div>
      </div>

      <ConfirmationModal
        isOpen={deletingUser !== null}
        title="Delete this user?"
        message={
          deletingUser
            ? `This permanently removes the login account for ${deletingUser.displayName} (${deletingUser.email}).`
            : undefined
        }
        confirmLabel="Delete"
        confirmVariant="danger"
        onConfirm={() => {
          if (deletingUser) onDelete(deletingUser.id)
          setDeletingUser(null)
        }}
        onCancel={() => setDeletingUser(null)}
      />
    </div>
  )
}
