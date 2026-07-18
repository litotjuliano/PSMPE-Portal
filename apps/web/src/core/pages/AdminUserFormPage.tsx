import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { isAxiosError } from 'axios'
import { adminApi } from '../api/endpoints/adminApi'
import { Roles, type Role } from '../types/auth'
import { AdminUserFormCard, PageBreadcrumb, PageMeta } from '../../integrations/template'

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

// Mirrors the backend's Identity password policy (see DependencyInjection.AddInfrastructure):
// RequiredLength = 8, RequireNonAlphanumeric = false; RequireDigit/Uppercase/Lowercase are
// Identity's unmodified defaults (true).
function passwordErrors(password: string): string[] {
  const errors: string[] = []
  if (password.length < 8) errors.push('Password must be at least 8 characters.')
  if (!/[0-9]/.test(password)) errors.push('Password must contain at least one digit.')
  if (!/[A-Z]/.test(password)) errors.push('Password must contain at least one uppercase letter.')
  if (!/[a-z]/.test(password)) errors.push('Password must contain at least one lowercase letter.')
  return errors
}

export interface AdminUserFormFieldErrors {
  displayName?: string
  email?: string
  password?: string
}

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
  const [submitting, setSubmitting] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<AdminUserFormFieldErrors>({})
  const [serverErrors, setServerErrors] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)

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
    setError(null)
    setServerErrors([])

    const nextFieldErrors: AdminUserFormFieldErrors = {}
    if (!displayName.trim()) nextFieldErrors.displayName = 'Name is required.'
    if (!EMAIL_PATTERN.test(email)) nextFieldErrors.email = 'Enter a valid email address.'
    const relevantPassword = isNew ? password : newPassword
    if (isNew || relevantPassword) {
      const passwordIssues = passwordErrors(relevantPassword)
      if (passwordIssues.length > 0) nextFieldErrors.password = passwordIssues.join(' ')
    }

    setFieldErrors(nextFieldErrors)
    if (Object.keys(nextFieldErrors).length > 0) {
      return
    }

    setSubmitting(true)
    try {
      if (isNew) {
        await adminApi.createUser({ email, displayName, password, role })
      } else if (id) {
        await adminApi.updateUser(id, { displayName, email, newPassword: newPassword || undefined })
      }
      navigate('/admin/users')
    } catch (err) {
      if (isAxiosError(err) && err.response?.status === 409) {
        setError(err.response.data?.message ?? 'An account with this email already exists.')
      } else if (isAxiosError(err) && err.response?.status === 400 && err.response.data?.errors) {
        const messages = Object.values(err.response.data.errors as Record<string, string[]>).flat()
        setServerErrors(messages.length > 0 ? messages : ['Please check the highlighted fields and try again.'])
      } else {
        setError('Something went wrong saving this user. Please try again.')
      }
    } finally {
      setSubmitting(false)
    }
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
            fieldErrors={fieldErrors}
            serverErrors={serverErrors}
            error={error}
            submitting={submitting}
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
