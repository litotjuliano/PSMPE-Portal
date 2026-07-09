export const Roles = {
  SuperAdmin: 'Super Admin',
  Admin: 'Admin',
  ContentCreator: 'Content Creator',
} as const

export type Role = (typeof Roles)[keyof typeof Roles]

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
}

export interface AuthUser {
  email: string
  displayName: string
  roles: Role[]
}
