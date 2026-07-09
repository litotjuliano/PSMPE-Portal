import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from './useAuth'
import type { Role } from '../types/auth'

interface ProtectedRouteProps {
  requiredRoles?: Role[]
}

export function ProtectedRoute({ requiredRoles }: ProtectedRouteProps) {
  const { isAuthenticated, user } = useAuth()

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  if (requiredRoles && !requiredRoles.some((role) => user?.roles.includes(role))) {
    return <Navigate to="/" replace />
  }

  return <Outlet />
}
