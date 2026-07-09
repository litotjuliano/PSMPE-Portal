import { apiClient } from '../apiClient'
import type { AuthResponse, LoginRequest, RegisterRequest } from '../../types/auth'

export const authApi = {
  login: (request: LoginRequest) =>
    apiClient.post<AuthResponse>('/api/auth/login', request).then((res) => res.data),

  register: (request: RegisterRequest) =>
    apiClient.post<AuthResponse>('/api/auth/register', request).then((res) => res.data),
}
