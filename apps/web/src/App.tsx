import 'flatpickr/dist/flatpickr.css'
import { RouterProvider } from 'react-router-dom'
import { AuthProvider } from './core/auth/AuthContext'
import { router } from './core/routes/router'
import { AppErrorBoundary } from './core/errorReporting/AppErrorBoundary'

export function App() {
  return (
    <AppErrorBoundary>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </AppErrorBoundary>
  )
}
