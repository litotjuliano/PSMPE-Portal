export const Roles = {
  SuperAdmin: 'Super Admin',
  Admin: 'Admin',
  Manager: 'Manager',
  Accounts: 'Accounts',
  Member: 'Member',
} as const

export type Role = (typeof Roles)[keyof typeof Roles]

/** Super Admin is never assignable/visible through the app - provisioned only via seeding/config/direct DB. */
export const AssignableRoles = Object.values(Roles).filter((role) => role !== Roles.SuperAdmin)

export interface AuthResponse {
  token: string
  expiresAt: string
  email: string
  displayName: string
  roles: Role[]
}

export interface LoginRequest {
  email: string
  password: string
}

export interface RegisterRequest {
  email: string
  password: string
  displayName: string
  username?: string
}

/**
 * Registration no longer returns a usable token - the account can't be used until the email is
 * confirmed. devVerificationLink is only populated outside Production (no real email provider is
 * configured yet), so the flow is testable without one.
 */
export interface RegisterResponse {
  email: string
  message: string
  devVerificationLink?: string
}

export interface VerifyEmailRequest {
  userId: string
  token: string
}

export interface ResendVerificationEmailRequest {
  email: string
}

export interface ResendVerificationEmailResponse {
  message: string
  devVerificationLink?: string
}

export interface AuthUser {
  email: string
  displayName: string
  roles: Role[]
}
