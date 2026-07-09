import { apiClient } from '../apiClient'
import type { Role } from '../../types/auth'

export interface UserSummary {
  id: string
  email: string
  displayName: string
  roles: Role[]
}

export const adminApi = {
  getUsers: () => apiClient.get<UserSummary[]>('/api/admin/users').then((res) => res.data),

  assignRole: (userId: string, role: Role) =>
    apiClient.post(`/api/admin/users/${userId}/roles`, { role }).then((res) => res.data),
}
