import type { FormEvent } from 'react'
import { AssignableRoles, type Role } from '../../../core/types/auth'
import type { AdminUserFormFieldErrors } from '../../../core/pages/AdminUserFormPage'

interface AdminUserFormCardProps {
  isNew: boolean
  displayName: string
  email: string
  password: string
  newPassword: string
  role: Role
  fieldErrors?: AdminUserFormFieldErrors
  serverErrors?: string[]
  error?: string | null
  submitting?: boolean
  onDisplayNameChange: (value: string) => void
  onEmailChange: (value: string) => void
  onPasswordChange: (value: string) => void
  onNewPasswordChange: (value: string) => void
  onRoleChange: (value: Role) => void
  onSubmit: (event: FormEvent) => void
}

export const AdminUserFormCard = ({
  isNew,
  displayName,
  email,
  password,
  newPassword,
  role,
  fieldErrors = {},
  serverErrors = [],
  error = null,
  submitting = false,
  onDisplayNameChange,
  onEmailChange,
  onPasswordChange,
  onNewPasswordChange,
  onRoleChange,
  onSubmit,
}: AdminUserFormCardProps) => {
  return (
    <div className="card max-w-2xl">
      <div className="card-header">
        <h6 className="card-title">{isNew ? 'New user' : 'Edit user'}</h6>
      </div>
      <form onSubmit={onSubmit} className="card-body space-y-4">
        <div>
          <label htmlFor="displayName" className="block font-medium text-default-900 text-sm mb-2">
            Name
          </label>
          <input
            id="displayName"
            type="text"
            className="form-input"
            required
            value={displayName}
            onChange={(e) => onDisplayNameChange(e.target.value)}
          />
          {fieldErrors.displayName && <p className="text-xs text-danger mt-1">{fieldErrors.displayName}</p>}
        </div>

        <div>
          <label htmlFor="email" className="block font-medium text-default-900 text-sm mb-2">
            Email
          </label>
          <input
            id="email"
            type="email"
            className="form-input"
            required
            value={email}
            onChange={(e) => onEmailChange(e.target.value)}
          />
          {fieldErrors.email && <p className="text-xs text-danger mt-1">{fieldErrors.email}</p>}
        </div>

        {isNew ? (
          <>
            <div>
              <label htmlFor="password" className="block font-medium text-default-900 text-sm mb-2">
                Password
              </label>
              <input
                id="password"
                type="password"
                className="form-input"
                required
                minLength={8}
                value={password}
                onChange={(e) => onPasswordChange(e.target.value)}
              />
              <p className="text-xs text-default-500 mt-1">
                At least 8 characters, with an uppercase letter, a lowercase letter, and a digit.
              </p>
              {fieldErrors.password && <p className="text-xs text-danger mt-1">{fieldErrors.password}</p>}
            </div>

            <div>
              <label htmlFor="role" className="block font-medium text-default-900 text-sm mb-2">
                Role
              </label>
              <select id="role" className="form-input" value={role} onChange={(e) => onRoleChange(e.target.value as Role)}>
                {AssignableRoles.map((r) => (
                  <option key={r} value={r}>
                    {r}
                  </option>
                ))}
              </select>
            </div>
          </>
        ) : (
          <div>
            <label htmlFor="newPassword" className="block font-medium text-default-900 text-sm mb-2">
              Reset password (optional)
            </label>
            <input
              id="newPassword"
              type="password"
              className="form-input"
              minLength={8}
              placeholder="Leave blank to keep the current password"
              value={newPassword}
              onChange={(e) => onNewPasswordChange(e.target.value)}
            />
            <p className="text-xs text-default-500 mt-1">
              At least 8 characters, with an uppercase letter, a lowercase letter, and a digit.
            </p>
            {fieldErrors.password && <p className="text-xs text-danger mt-1">{fieldErrors.password}</p>}
          </div>
        )}

        {error && <p className="text-sm text-danger">{error}</p>}
        {serverErrors.length > 0 && (
          <ul className="text-sm text-danger list-disc pl-5">
            {serverErrors.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </ul>
        )}

        <button type="submit" className="btn bg-primary text-white" disabled={submitting}>
          {submitting ? 'Saving…' : 'Save'}
        </button>
      </form>
    </div>
  )
}
