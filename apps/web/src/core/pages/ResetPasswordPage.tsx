import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { isAxiosError } from 'axios'
import { authApi } from '../api/endpoints/authApi'
import { passwordErrors } from '../utils/passwordPolicy'
import { AuthSplitLayout, AuthTextInput, PageMeta } from '../../integrations/template'

interface FieldErrors {
  password?: string
  confirmPassword?: string
}

export function ResetPasswordPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const userId = searchParams.get('userId')
  const token = searchParams.get('token')

  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  if (!userId || !token) {
    return (
      <>
        <PageMeta title="Reset Password" />
        <AuthSplitLayout heading="Reset Password" subheading="This link is invalid.">
          <p className="text-sm text-danger mb-4">
            This password reset link is missing or malformed. Please request a new one.
          </p>
          <p className="text-center text-sm text-default-500">
            <Link to="/forgot-password" className="font-semibold text-primary">
              Request a new link
            </Link>
          </p>
        </AuthSplitLayout>
      </>
    )
  }

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)

    const nextFieldErrors: FieldErrors = {}
    const issues = passwordErrors(password)
    if (issues.length > 0) nextFieldErrors.password = issues.join(' ')
    if (password !== confirmPassword) nextFieldErrors.confirmPassword = 'Passwords do not match.'

    setFieldErrors(nextFieldErrors)
    if (Object.keys(nextFieldErrors).length > 0) {
      return
    }

    setSubmitting(true)
    try {
      const response = await authApi.resetPassword({ userId, token, newPassword: password })
      navigate('/login', { state: { successMessage: response.message } })
    } catch (err) {
      if (isAxiosError(err) && err.response?.status === 400 && err.response.data?.errors) {
        const messages = Object.values(err.response.data.errors as Record<string, string[]>).flat()
        setError(messages.length > 0 ? messages.join(' ') : 'Please check your new password and try again.')
      } else if (isAxiosError(err) && err.response?.status === 429) {
        // Ahead of the generic arm, which would otherwise tell someone holding a perfectly good
        // reset link that it is invalid or expired - and send them back to request another,
        // against a per-address cap of three an hour.
        setError('Too many attempts from your connection. Please wait a few minutes and try again - your reset link is still valid.')
      } else if (isAxiosError(err) && err.response) {
        setError((err.response.data as { message?: string } | undefined)?.message ?? 'This password reset link is invalid or has expired.')
      } else {
        setError('Something went wrong. Please try again.')
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <>
      <PageMeta title="Reset Password" />
      <AuthSplitLayout heading="Reset Password" subheading="Enter and confirm your new password.">
        <form onSubmit={handleSubmit} className="text-start w-full">
          <AuthTextInput
            label="New Password"
            type="password"
            placeholder="Enter your password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            error={fieldErrors.password}
          />

          <AuthTextInput
            label="Confirm New Password"
            type="password"
            placeholder="Enter your confirm password"
            required
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            error={fieldErrors.confirmPassword}
          />

          {error && <p className="text-sm text-danger mb-4">{error}</p>}

          <button type="submit" disabled={submitting} className="btn bg-primary text-white w-full min-h-11">
            {submitting ? 'Resetting…' : 'Change Password'}
          </button>
        </form>

        <p className="mt-6 text-center text-sm text-default-500">
          <Link to="/login" className="font-semibold text-primary">
            Back to sign in
          </Link>
        </p>
      </AuthSplitLayout>
    </>
  )
}
