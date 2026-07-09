import { createContext, useCallback, useMemo, useState, type ReactNode } from 'react'
import { authApi } from '../api/endpoints/authApi'
import { tokenStorage } from '../api/apiClient'
import type { AuthUser, LoginRequest, RegisterRequest } from '../types/auth'

interface AuthContextValue {
  user: AuthUser | null
  isAuthenticated: boolean
  login: (request: LoginRequest) => Promise<void>
  register: (request: RegisterRequest) => Promise<void>
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

  const register = useCallback(
    async (request: RegisterRequest) => {
      const response = await authApi.register(request)
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
    () => ({ user, isAuthenticated: user !== null, login, register, logout }),
    [user, login, register, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
