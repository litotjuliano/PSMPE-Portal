import { createContext, useCallback, useMemo, useState, type ReactNode } from 'react'
import { authApi } from '../api/endpoints/authApi'
import { tokenStorage } from '../api/apiClient'
import type { AuthUser, LoginRequest, RegisterRequest, RegisterResponse } from '../types/auth'

interface AuthContextValue {
  user: AuthUser | null
  isAuthenticated: boolean
  login: (request: LoginRequest) => Promise<void>
  register: (request: RegisterRequest) => Promise<RegisterResponse>
  verifyEmail: (userId: string, token: string) => Promise<void>
  logout: () => void
}

// eslint-disable-next-line react-refresh/only-export-components
export const AuthContext = createContext<AuthContextValue | undefined>(undefined)

const USER_STORAGE_KEY = 'psmpe.auth.user'

function readStoredUser(): AuthUser | null {
  const raw = localStorage.getItem(USER_STORAGE_KEY)
  return raw ? (JSON.parse(raw) as AuthUser) : null
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(() => readStoredUser())

  const persist = useCallback((token: string, authUser: AuthUser) => {
    tokenStorage.set(token)
    localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(authUser))
    setUser(authUser)
  }, [])

  const login = useCallback(
    async (request: LoginRequest) => {
      const response = await authApi.login(request)
      persist(response.token, {
        email: response.email,
        displayName: response.displayName,
        roles: response.roles,
      })
    },
    [persist],
  )

  const register = useCallback(async (request: RegisterRequest) => {
    // No token yet - the account can't be used until the email is verified (see verifyEmail).
    return authApi.register(request)
  }, [])

  const verifyEmail = useCallback(
    async (userId: string, token: string) => {
      const response = await authApi.verifyEmail(userId, token)
      persist(response.token, {
        email: response.email,
        displayName: response.displayName,
        roles: response.roles,
      })
    },
    [persist],
  )

  const logout = useCallback(() => {
    tokenStorage.clear()
    localStorage.removeItem(USER_STORAGE_KEY)
    setUser(null)
  }, [])

  const value = useMemo(
    () => ({ user, isAuthenticated: user !== null, login, register, verifyEmail, logout }),
    [user, login, register, verifyEmail, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
