import { useAuth } from '../auth/useAuth'

export function DashboardPage() {
  const { user } = useAuth()

  return (
    <div>
      <h1 className="text-2xl font-semibold text-gray-900">Welcome, {user?.displayName}</h1>
      <p className="mt-2 text-sm text-gray-600">
        Roles: {user?.roles.join(', ')}
      </p>
      {/* TODO: replace with real widgets (recent content, activity) once available. */}
    </div>
  )
}
