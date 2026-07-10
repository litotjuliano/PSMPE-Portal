import { useEffect, useState } from 'react'
import { adminApi, type RoleSummary } from '../api/endpoints/adminApi'
import { AdminRolesTable, PageBreadcrumb, PageMeta } from '../../integrations/template'
import { useAuth } from '../auth/useAuth'
import { Roles } from '../types/auth'

export function AdminRolesPage() {
  const { user } = useAuth()
  const [roles, setRoles] = useState<RoleSummary[]>([])
  const [permissions, setPermissions] = useState<string[]>([])
  const [loading, setLoading] = useState(true)

  const canEdit = user?.roles.includes(Roles.SuperAdmin) ?? false

  const refetch = () => adminApi.getRoles().then(setRoles)

  useEffect(() => {
    Promise.all([adminApi.getRoles().then(setRoles), adminApi.getPermissions().then(setPermissions)]).finally(() =>
      setLoading(false),
    )
  }, [])

  const handleSave = (roleId: string, rolePermissions: string[]) =>
    adminApi.updateRolePermissions(roleId, rolePermissions).then(refetch)

  return (
    <>
      <PageMeta title="Roles & Permissions" />
      <main>
        <PageBreadcrumb title="Roles & Permissions" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <AdminRolesTable roles={roles} allPermissions={permissions} canEdit={canEdit} onSave={handleSave} />
        )}
      </main>
    </>
  )
}
