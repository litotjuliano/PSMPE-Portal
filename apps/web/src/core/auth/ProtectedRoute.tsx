import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from './useAuth'
import type { Role } from '../types/auth'

interface ProtectedRouteProps {
  requiredRoles?: Role[]
  /** Any one of these permissions is enough. Combined with requiredRoles, both must pass. */
  requiredPermissions?: readonly string[]
}

export function ProtectedRoute({ requiredRoles, requiredPermissions }: ProtectedRouteProps) {
  const { isAuthenticated, user } = useAuth()

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  if (requiredRoles && !requiredRoles.some((role) => user?.roles.includes(role))) {
    return <Navigate to="/" replace />
  }

  if (requiredPermissions && !requiredPermissions.some((p) => user?.permissions?.includes(p))) {
    return <Navigate to="/" replace />
  }

  return <Outlet />
}
