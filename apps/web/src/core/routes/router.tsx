import { createBrowserRouter } from 'react-router-dom'
import { AppShell, DashboardPage, LoginPage } from '../../integrations/template'
import { ProtectedRoute } from '../auth/ProtectedRoute'
import { ContentListPage } from '../pages/ContentListPage'
import { ContentEditPage } from '../pages/ContentEditPage'
import { AdminUsersPage } from '../pages/AdminUsersPage'
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
            children: [{ path: '/admin/users', element: <AdminUsersPage /> }],
          },
        ],
      },
    ],
  },
])
