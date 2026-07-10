import type { FormEvent } from 'react'
import { Roles, type Role } from '../../../core/types/auth'

interface AdminUserFormCardProps {
  isNew: boolean
  displayName: string
  email: string
  password: string
  newPassword: string
  role: Role
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
            </div>

            <div>
              <label htmlFor="role" className="block font-medium text-default-900 text-sm mb-2">
                Role
              </label>
              <select id="role" className="form-input" value={role} onChange={(e) => onRoleChange(e.target.value as Role)}>
                {Object.values(Roles).map((r) => (
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
          </div>
        )}

        <button type="submit" className="btn bg-primary text-white">
          Save
        </button>
      </form>
    </div>
  )
}
