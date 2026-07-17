import { useEffect, useState } from 'react'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { isAxiosError } from 'axios'
import { useAuth } from '../auth/useAuth'
import { authApi } from '../api/endpoints/authApi'
import { BlueprintBg, PageMeta } from '../../integrations/template'
import logoDark from '../../integrations/template/assets/images/logo-dark.png'
import logoLight from '../../integrations/template/assets/images/logo-light.png'
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

  return (
    <>
      <PageMeta title="Verify Email" />
      <div className="relative min-h-screen w-full flex flex-col justify-center items-center py-16 md:py-10">
        <div className="card md:w-lg w-screen z-10">
          <div className="text-center px-10 py-12">
            <Link to="/" className="flex justify-center">
              <img src={logoDark} alt="logo dark" className="h-6 flex dark:hidden" width={111} />
              <img src={logoLight} alt="logo light" className="h-6 hidden dark:flex" width={111} />
            </Link>

            <div className="mt-8 text-center">
              <h4 className="mb-3 text-xl font-semibold text-primary">Verify Email</h4>

              {verifying ? (
                <p className="text-base text-default-500 mb-4">Verifying your email…</p>
              ) : verifyError ? (
                <>
                  <p className="text-sm text-danger mb-4">{verifyError}</p>
                  <p className="text-base text-default-500 mb-4">
                    Need a new link?{' '}
                    <button type="button" onClick={handleResend} disabled={resending || !email} className="text-primary disabled:opacity-50">
                      Resend verification email
                    </button>
                  </p>
                </>
              ) : (
                <>
                  <p className="text-base text-default-500 mb-4">
                    {email ? (
                      <>
                        We sent a verification link to <span className="font-semibold text-default-800">{email}</span>. Click it to
                        activate your account.
                      </>
                    ) : (
                      'Check your email for a verification link to activate your account.'
                    )}
                  </p>
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
              )}

              <div className="mt-10 text-center">
                <img src={emailImg} alt="" className="block w-1/2 mx-auto" />
              </div>
            </div>
          </div>
        </div>

        <BlueprintBg />
      </div>
    </>
  )
}
