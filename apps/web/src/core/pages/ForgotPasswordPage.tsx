import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { LuChevronLeft } from 'react-icons/lu'
import { authApi } from '../api/endpoints/authApi'
import { AuthSplitLayout, AuthTextInput, PageMeta } from '../../integrations/template'

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [devLink, setDevLink] = useState<string | null>(null)

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setSubmitting(true)
    try {
      const response = await authApi.forgotPassword(email)
      setMessage(response.message)
      setDevLink(response.devResetLink ?? null)
    } catch {
      setMessage('Could not process your request right now. Please try again in a moment.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <>
      <PageMeta title="Forgot Password" />
      <AuthSplitLayout heading="Forgot Your Password?" subheading="Fill in your email address to reset your account">
        {message ? (
          <div className="w-full">
            <p className="text-sm text-default-600 mb-4">{message}</p>
            {devLink && (
              <p className="text-xs text-default-500 mb-4 break-all">
                Dev only (no email provider configured):{' '}
                <a href={devLink} className="text-primary underline">
                  {devLink}
                </a>
              </p>
            )}
            <p className="text-center text-sm text-default-500">
              <Link to="/login" className="font-semibold text-primary">
                Back to sign in
              </Link>
            </p>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="text-start w-full">
            <AuthTextInput
              label="Account Email"
              type="email"
              id="email"
              placeholder="you@example.com"
              autoComplete="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />

            <div className="flex justify-between items-center mt-8">
              <Link to="/login" className="inline-flex justify-center items-center gap-1 text-primary hover:text-primary/80 font-semibold">
                <LuChevronLeft className="size-4" aria-hidden="true" /> Back to sign in
              </Link>
              <button
                type="submit"
                disabled={submitting}
                className="btn bg-primary text-white min-h-11 disabled:opacity-70"
              >
                {submitting ? 'Sending…' : 'Send Reset Link'}
              </button>
            </div>
          </form>
        )}
      </AuthSplitLayout>
    </>
  )
}
