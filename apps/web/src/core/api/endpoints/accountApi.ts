import { apiClient } from '../apiClient'
import type { Role } from '../../types/auth'

export interface AccountDto {
  email: string
  displayName: string
  roles: Role[]
}

export interface UpdateAccountRequest {
  displayName: string
}

export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}

/**
 * Self-service account management, available to every role. Distinct from adminApi, which acts on
 * *other* people's user records. There is no GET here by design - the sign-in response already
 * supplies email/displayName/roles, and the update returns the new state.
 */
export const accountApi = {
  updateAccount: (request: UpdateAccountRequest) =>
    apiClient.put<AccountDto>('/api/account/me', request).then((res) => res.data),

  changePassword: (request: ChangePasswordRequest) =>
    apiClient.post('/api/account/me/password', request).then(() => undefined),
}
