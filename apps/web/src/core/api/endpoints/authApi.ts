import { apiClient } from '../apiClient'
import type {
  AuthResponse,
  LoginRequest,
  RegisterRequest,
  RegisterResponse,
  ResendVerificationEmailResponse,
} from '../../types/auth'

export const authApi = {
  login: (request: LoginRequest) =>
    apiClient.post<AuthResponse>('/api/auth/login', request).then((res) => res.data),

  register: (request: RegisterRequest) =>
    apiClient.post<RegisterResponse>('/api/auth/register', request).then((res) => res.data),

  verifyEmail: (userId: string, token: string) =>
    apiClient.post<AuthResponse>('/api/auth/verify-email', { userId, token }).then((res) => res.data),

  resendVerificationEmail: (email: string) =>
    apiClient
      .post<ResendVerificationEmailResponse>('/api/auth/resend-verification-email', { email })
      .then((res) => res.data),

  isUsernameAvailable: (username: string) =>
    apiClient.get<boolean>('/api/auth/username-available', { params: { username } }).then((res) => res.data),
}
