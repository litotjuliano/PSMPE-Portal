import { NavLink } from 'react-router-dom'
import { useAuth } from '../../auth/useAuth'
import { Roles } from '../../types/auth'

const navItemClass = ({ isActive }: { isActive: boolean }) =>
  `block rounded-md px-3 py-2 text-sm font-medium ${
    isActive ? 'bg-indigo-50 text-indigo-700' : 'text-gray-700 hover:bg-gray-50'
  }`

export function Sidebar() {
  const { user } = useAuth()
  const isAdmin = user?.roles.some((r) => r === Roles.Admin || r === Roles.SuperAdmin)

  return (
    <nav className="w-56 shrink-0 border-r border-gray-200 bg-white p-4">
      <div className="mb-6 px-3 text-lg font-bold text-gray-900">PSMPE Portal</div>
      <div className="space-y-1">
        <NavLink to="/" end className={navItemClass}>
          Dashboard
        </NavLink>
        <NavLink to="/content" className={navItemClass}>
          Content
        </NavLink>
        {isAdmin && (
          <NavLink to="/admin/users" className={navItemClass}>
            Users
          </NavLink>
        )}
      </div>
    </nav>
  )
}
