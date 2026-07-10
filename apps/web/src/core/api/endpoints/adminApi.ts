import { apiClient } from '../apiClient'
import type { Role } from '../../types/auth'

export interface UserSummary {
  id: string
  email: string
  displayName: string
  roles: Role[]
  createdAt: string
}

export interface RoleSummary {
  id: string
  name: Role
  permissions: string[]
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export interface GetUsersParams {
  page?: number
  pageSize?: number
  sortBy?: 'displayName' | 'email' | 'createdAt'
  sortDir?: 'asc' | 'desc'
}

export interface CreateUserRequest {
  email: string
  displayName: string
  password: string
  role?: Role
}

export interface UpdateUserRequest {
  displayName: string
  email: string
  newPassword?: string
}

export const adminApi = {
  getUsers: (params: GetUsersParams = {}) =>
    apiClient.get<PagedResult<UserSummary>>('/api/admin/users', { params }).then((res) => res.data),

  getUserById: (id: string) => apiClient.get<UserSummary>(`/api/admin/users/${id}`).then((res) => res.data),

  createUser: (request: CreateUserRequest) =>
    apiClient.post<UserSummary>('/api/admin/users', request).then((res) => res.data),

  updateUser: (id: string, request: UpdateUserRequest) =>
    apiClient.put(`/api/admin/users/${id}`, request).then((res) => res.data),

  deleteUser: (id: string) => apiClient.delete(`/api/admin/users/${id}`).then((res) => res.data),

  assignRole: (userId: string, role: Role) =>
    apiClient.post(`/api/admin/users/${userId}/roles`, { role }).then((res) => res.data),

  removeRole: (userId: string, role: Role) =>
    apiClient.delete(`/api/admin/users/${userId}/roles`, { data: { role } }).then((res) => res.data),

  getRoles: () => apiClient.get<RoleSummary[]>('/api/admin/roles').then((res) => res.data),

  updateRolePermissions: (roleId: string, permissions: string[]) =>
    apiClient.put(`/api/admin/roles/${roleId}/permissions`, { permissions }).then((res) => res.data),

  getPermissions: () => apiClient.get<string[]>('/api/admin/permissions').then((res) => res.data),
}
