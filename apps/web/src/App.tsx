import 'flatpickr/dist/flatpickr.css'
import { RouterProvider } from 'react-router-dom'
import { AuthProvider } from './core/auth/AuthContext'
import { router } from './core/routes/router'

export function App() {
  return (
    <AuthProvider>
      <RouterProvider router={router} />
    </AuthProvider>
  )
}
