import { createBrowserRouter, Navigate } from 'react-router-dom'
import { AppShell, DashboardPage, LoginPage } from '../../integrations/template'
import { ProtectedRoute } from '../auth/ProtectedRoute'
import { DataPrivacyConsentGate } from '../auth/DataPrivacyConsentGate'
import { ExpiredMembershipGate } from '../auth/ExpiredMembershipGate'
import { ContentListPage } from '../pages/ContentListPage'
import { ContentEditPage } from '../pages/ContentEditPage'
import { AdminUsersPage } from '../pages/AdminUsersPage'
import { AdminUserFormPage } from '../pages/AdminUserFormPage'
import { AdminRolesPage } from '../pages/AdminRolesPage'
import { MembersPage } from '../pages/MembersPage'
import { MembershipFeesPage } from '../pages/MembershipFeesPage'
import { MemberFormPage } from '../pages/MemberFormPage'
import { MyProfilePage } from '../pages/MyProfilePage'
import { EventsPage } from '../pages/EventsPage'
import { EventRosterPage } from '../pages/EventRosterPage'
import { MyCpdPage } from '../pages/MyCpdPage'
import { RegisterPage } from '../pages/RegisterPage'
import { VerifyEmailPage } from '../pages/VerifyEmailPage'
import { ForgotPasswordPage } from '../pages/ForgotPasswordPage'
import { ResetPasswordPage } from '../pages/ResetPasswordPage'
import { NotificationsPage } from '../pages/NotificationsPage'
import { SystemLogsPage } from '../pages/SystemLogsPage'
import { Roles } from '../types/auth'

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/register', element: <RegisterPage /> },
  { path: '/verify-email', element: <VerifyEmailPage /> },
  { path: '/forgot-password', element: <ForgotPasswordPage /> },
  { path: '/reset-password', element: <ResetPasswordPage /> },
  {
    element: <ProtectedRoute />,
    children: [
      {
        // Wraps every authenticated page once. Sits outside AppShell so the prompt isn't
        // re-mounted by the nested admin ProtectedRoutes on each navigation.
        element: <DataPrivacyConsentGate />,
        children: [
          {
            // Consent resolves first, then membership-access state is resolved and exposed via
            // useMembershipAccess - it doesn't redirect or hide pages, see ExpiredMembershipGate.
            element: <ExpiredMembershipGate />,
            children: [
              {
                element: <AppShell />,
                children: [
                  { path: '/', element: <DashboardPage /> },
                  { path: '/content', element: <ContentListPage /> },
                  { path: '/content/:id', element: <ContentEditPage /> },
                  { path: '/profile', element: <MyProfilePage /> },
                  { path: '/events', element: <EventsPage /> },
                  { path: '/my-cpd', element: <MyCpdPage /> },
                  {
                    element: <ProtectedRoute requiredRoles={[Roles.Admin, Roles.SuperAdmin, Roles.Approval]} />,
                    children: [
                      { path: '/admin/users', element: <AdminUsersPage /> },
                      { path: '/admin/users/:id', element: <AdminUserFormPage /> },
                      { path: '/admin/roles', element: <AdminRolesPage /> },
                      { path: '/members', element: <MembersPage /> },
                      { path: '/members/:id', element: <MemberFormPage /> },
                      { path: '/events/:id/roster', element: <EventRosterPage /> },
                      // Both queues folded into MembersPage's tabs. Kept as redirects rather than
                      // deleted so bookmarks and any older in-app link still land somewhere useful.
                      { path: '/membership-approvals', element: <Navigate to="/members?queue=approval" replace /> },
                      { path: '/prc-verifications', element: <Navigate to="/members?queue=rmp" replace /> },
                      { path: '/membership-fees', element: <MembershipFeesPage /> },
                      { path: '/notifications', element: <NotificationsPage /> },
                    ],
                  },
                  {
                    element: <ProtectedRoute requiredRoles={[Roles.SuperAdmin]} />,
                    children: [
                      { path: '/admin/system-logs', element: <SystemLogsPage /> },
                    ],
                  },
                ],
              },
            ],
          },
        ],
      },
    ],
  },
])
