import { useState } from 'react'
import type { RoleSummary } from '../../../core/api/endpoints/adminApi'

interface AdminRolesTableProps {
  roles: RoleSummary[]
  allPermissions: string[]
  canEdit: boolean
  onSave: (roleId: string, permissions: string[]) => Promise<void>
}

function groupByResource(permissions: string[]) {
  const groups = new Map<string, string[]>()
  for (const permission of permissions) {
    const [resource] = permission.split(':')
    const group = groups.get(resource) ?? []
    group.push(permission)
    groups.set(resource, group)
  }
  return groups
}

export const AdminRolesTable = ({ roles, allPermissions, canEdit, onSave }: AdminRolesTableProps) => {
  const [pending, setPending] = useState<Record<string, string[]>>({})
  const permissionGroups = groupByResource(allPermissions)

  const permissionsFor = (role: RoleSummary) => pending[role.id] ?? role.permissions
  const isDirty = (role: RoleSummary) => pending[role.id] !== undefined

  const toggle = (role: RoleSummary, permission: string) => {
    const current = permissionsFor(role)
    const next = current.includes(permission) ? current.filter((p) => p !== permission) : [...current, permission]
    setPending((prev) => ({ ...prev, [role.id]: next }))
  }

  const clearPending = (roleId: string) => {
    setPending((prev) => {
      const next = { ...prev }
      delete next[roleId]
      return next
    })
  }

  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">Roles &amp; Permissions</h6>
      </div>
      <div className="flex flex-col divide-y divide-default-200">
        {roles.map((role) => (
          <div key={role.id} className="p-4">
            <div className="flex items-center justify-between mb-3">
              <span className="font-semibold text-sm text-default-800">{role.name}</span>
              {canEdit && (
                <button
                  type="button"
                  className="btn bg-primary text-white btn-sm disabled:opacity-50"
                  disabled={!isDirty(role)}
                  onClick={() => onSave(role.id, permissionsFor(role)).then(() => clearPending(role.id))}
                >
                  Save
                </button>
              )}
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
              {[...permissionGroups.entries()].map(([resource, permissions]) => (
                <div key={resource}>
                  <div className="text-xs font-semibold text-default-500 uppercase mb-1.5">{resource}</div>
                  <div className="flex flex-col gap-1.5">
                    {permissions.map((permission) => (
                      <label key={permission} className="flex items-center gap-1.5 text-xs text-default-700 cursor-pointer">
                        <input
                          type="checkbox"
                          className="form-checkbox size-3.5"
                          checked={permissionsFor(role).includes(permission)}
                          disabled={!canEdit}
                          onChange={() => toggle(role, permission)}
                        />
                        {permission}
                      </label>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>
        ))}
        {roles.length === 0 && <div className="p-6 text-center text-default-500 text-sm">No roles yet.</div>}
      </div>
    </div>
  )
}
