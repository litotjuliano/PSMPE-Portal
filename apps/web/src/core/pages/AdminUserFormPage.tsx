import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { adminApi } from '../api/endpoints/adminApi'
import { Roles, type Role } from '../types/auth'
import { AdminUserFormCard, PageBreadcrumb, PageMeta } from '../../integrations/template'

export function AdminUserFormPage() {
  const { id } = useParams()
  const isNew = !id || id === 'new'
  const navigate = useNavigate()

  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [role, setRole] = useState<Role>(Roles.Member)
  const [loading, setLoading] = useState(!isNew)

  useEffect(() => {
    if (!isNew && id) {
      adminApi.getUserById(id).then((user) => {
        setDisplayName(user.displayName)
        setEmail(user.email)
        setLoading(false)
      })
    }
  }, [id, isNew])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (isNew) {
      await adminApi.createUser({ email, displayName, password, role })
    } else if (id) {
      await adminApi.updateUser(id, { displayName, email, newPassword: newPassword || undefined })
    }
    navigate('/admin/users')
  }

  return (
    <>
      <PageMeta title={isNew ? 'New user' : 'Edit user'} />
      <main>
        <PageBreadcrumb title={isNew ? 'New user' : 'Edit user'} subtitle="Users" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <AdminUserFormCard
            isNew={isNew}
            displayName={displayName}
            email={email}
            password={password}
            newPassword={newPassword}
            role={role}
            onDisplayNameChange={setDisplayName}
            onEmailChange={setEmail}
            onPasswordChange={setPassword}
            onNewPasswordChange={setNewPassword}
            onRoleChange={setRole}
            onSubmit={handleSubmit}
          />
        )}
      </main>
    </>
  )
}
