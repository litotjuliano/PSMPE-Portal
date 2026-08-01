import { useEffect, useState } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { isAxiosError } from 'axios'
import { useAuth } from '../auth/useAuth'
import { authApi } from '../api/endpoints/authApi'
import { AuthSplitLayout, PageMeta } from '../../integrations/template'
import emailImg from '../../integrations/template/assets/images/auth-email.png'

interface LocationState {
  email?: string
  devVerificationLink?: string
}

export function VerifyEmailPage() {
  const { verifyEmail } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [searchParams] = useSearchParams()
  const state = (location.state as LocationState | null) ?? {}

  const userId = searchParams.get('userId')
  const token = searchParams.get('token')

  const [verifying, setVerifying] = useState(Boolean(userId && token))
  const [verifyError, setVerifyError] = useState<string | null>(null)

  const email = state.email ?? ''
  const [devLink, setDevLink] = useState(state.devVerificationLink ?? null)
  const [resending, setResending] = useState(false)
  const [resendMessage, setResendMessage] = useState<string | null>(null)

  useEffect(() => {
    if (!userId || !token) return
    verifyEmail(userId, token)
      .then(() => navigate('/'))
      .catch((err) => {
        if (isAxiosError(err) && err.response) {
          setVerifyError((err.response.data as { message?: string } | undefined)?.message ?? 'This verification link is invalid or has expired.')
        } else {
          setVerifyError('Could not reach the server. Please check your connection and try again.')
        }
        setVerifying(false)
      })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userId, token])

  const handleResend = async () => {
    if (!email) return
    setResending(true)
    setResendMessage(null)
    try {
      const response = await authApi.resendVerificationEmail(email)
      setResendMessage(response.message)
      setDevLink(response.devVerificationLink ?? null)
    } catch {
      setResendMessage('Could not resend the verification email. Please try again in a moment.')
    } finally {
      setResending(false)
    }
  }

  // AuthSplitLayout takes a single subheading string, so the three states collapse to one line
  // here and the detail lives in the body below.
  const subheading = verifying
    ? 'Verifying your email…'
    : verifyError
      ? "We couldn't verify this link."
      : 'Check your email to activate your account.'

  return (
    <>
      <PageMeta title="Verify Email" />
      <AuthSplitLayout heading="Verify Email" subheading={subheading}>
        <div className="w-full">
          {verifyError ? (
            <>
              <p className="text-sm text-danger mb-4">{verifyError}</p>
              <p className="text-base text-default-500 mb-4">
                Need a new link?{' '}
                <button type="button" onClick={handleResend} disabled={resending || !email} className="text-primary disabled:opacity-50">
                  {resending ? 'Sending…' : 'Resend verification email'}
                </button>
              </p>
            </>
          ) : !verifying ? (
            <>
              {/* Only rendered when we know the address - without it this would just restate the
                  subheading verbatim. */}
              {email && (
                <p className="text-base text-default-500 mb-4">
                  We sent a verification link to <span className="font-semibold text-default-800">{email}</span>. Click it to
                  activate your account.
                </p>
              )}
              <p className="text-base text-default-500 mb-4">
                Did you not receive an email?{' '}
                <button type="button" onClick={handleResend} disabled={resending || !email} className="text-primary disabled:opacity-50">
                  {resending ? 'Sending…' : 'Try again'}
                </button>
              </p>
              {resendMessage && <p className="text-sm text-default-500 mb-4">{resendMessage}</p>}
              {devLink && (
                <p className="text-xs text-default-500 mb-4 break-all">
                  Dev only (no email provider configured):{' '}
                  <a href={devLink} className="text-primary underline">
                    {devLink}
                  </a>
                </p>
              )}
            </>
          ) : null}

          <img src={emailImg} alt="" className="block w-1/2 max-w-[180px] mx-auto mt-6" />
        </div>
      </AuthSplitLayout>
    </>
  )
}
