import { createBrowserRouter } from 'react-router-dom'
import { AppShell, DashboardPage, LoginPage } from '../../integrations/template'
import { ProtectedRoute } from '../auth/ProtectedRoute'
import { ContentListPage } from '../pages/ContentListPage'
import { ContentEditPage } from '../pages/ContentEditPage'
import { AdminUsersPage } from '../pages/AdminUsersPage'
import { AdminUserFormPage } from '../pages/AdminUserFormPage'
import { AdminRolesPage } from '../pages/AdminRolesPage'
import { MembersPage } from '../pages/MembersPage'
import { MemberFormPage } from '../pages/MemberFormPage'
import { MyProfilePage } from '../pages/MyProfilePage'
import { RegisterPage } from '../pages/RegisterPage'
import { VerifyEmailPage } from '../pages/VerifyEmailPage'
import { MembershipApprovalsPage } from '../pages/MembershipApprovalsPage'
import { PrcVerificationsPage } from '../pages/PrcVerificationsPage'
import { NotificationsPage } from '../pages/NotificationsPage'
import { Roles } from '../types/auth'

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/register', element: <RegisterPage /> },
  { path: '/verify-email', element: <VerifyEmailPage /> },
  {
    element: <ProtectedRoute />,
    children: [
      {
        element: <AppShell />,
        children: [
          { path: '/', element: <DashboardPage /> },
          { path: '/content', element: <ContentListPage /> },
          { path: '/content/:id', element: <ContentEditPage /> },
          { path: '/profile', element: <MyProfilePage /> },
          {
            element: <ProtectedRoute requiredRoles={[Roles.Admin, Roles.SuperAdmin]} />,
            children: [
              { path: '/admin/users', element: <AdminUsersPage /> },
              { path: '/admin/users/:id', element: <AdminUserFormPage /> },
              { path: '/admin/roles', element: <AdminRolesPage /> },
              { path: '/members', element: <MembersPage /> },
              { path: '/members/:id', element: <MemberFormPage /> },
              { path: '/membership-approvals', element: <MembershipApprovalsPage /> },
              { path: '/prc-verifications', element: <PrcVerificationsPage /> },
              { path: '/notifications', element: <NotificationsPage /> },
            ],
          },
        ],
      },
    ],
  },
])
