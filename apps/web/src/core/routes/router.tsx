import { createBrowserRouter } from 'react-router-dom'
import { AppShell, DashboardPage, LoginPage } from '../../integrations/template'
import { ProtectedRoute } from '../auth/ProtectedRoute'
import { ContentListPage } from '../pages/ContentListPage'
import { ContentEditPage } from '../pages/ContentEditPage'
import { AdminUsersPage } from '../pages/AdminUsersPage'
import { AdminUserFormPage } from '../pages/AdminUserFormPage'
import { AdminRolesPage } from '../pages/AdminRolesPage'
import { Roles } from '../types/auth'

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    element: <ProtectedRoute />,
    children: [
      {
        element: <AppShell />,
        children: [
          { path: '/', element: <DashboardPage /> },
          { path: '/content', element: <ContentListPage /> },
          { path: '/content/:id', element: <ContentEditPage /> },
          {
            element: <ProtectedRoute requiredRoles={[Roles.Admin, Roles.SuperAdmin]} />,
            children: [
              { path: '/admin/users', element: <AdminUsersPage /> },
              { path: '/admin/users/:id', element: <AdminUserFormPage /> },
              { path: '/admin/roles', element: <AdminRolesPage /> },
            ],
          },
        ],
      },
    ],
  },
])
