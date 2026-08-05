import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { isAxiosError } from 'axios'
import PageMeta from '../components/shared/PageMeta'
import { AuthSplitLayout } from '../components/shared/AuthSplitLayout'
import { AuthTextInput } from '../components/shared/AuthTextInput'
import { useAuth } from '../../../core/auth/useAuth'
import { authApi } from '../../../core/api/endpoints/authApi'

/**
 * Mirrors the accounts IdentitySeeder.cs creates when SEED_ADMIN_PASSWORD/SEED_DEFAULT_PASSWORD
 * are set (dev/Testing only) - matches the repo's .env.example defaults. Keep in sync with
 * src/PSMPE.Portal.Infrastructure/Persistence/Seed/IdentitySeeder.cs if those values change.
 */
const DEV_SEED_ACCOUNTS = [
  { role: 'Super Admin', email: 'admin@psmpe.local', password: 'ChangeMe123!' },
  { role: 'Admin', email: 'admin.user@psmpe.local', password: 'ChangeMe123!' },
  { role: 'Manager', email: 'manager@psmpe.local', password: 'ChangeMe123!' },
  { role: 'Accounts', email: 'accounts@psmpe.local', password: 'ChangeMe123!' },
  { role: 'Member', email: 'member@psmpe.local', password: 'ChangeMe123!' },
] as const

interface LocationState {
  successMessage?: string
}

export const LoginPage = () => {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [successMessage] = useState((location.state as LocationState | null)?.successMessage ?? null)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [needsVerification, setNeedsVerification] = useState(false)
  const [resending, setResending] = useState(false)
  const [submitting, setSubmitting] = useState(false)

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setSubmitting(true)
    setError(null)
    setNeedsVerification(false)
    try {
      await login({ email, password })
      navigate('/')
    } catch (err) {
      if (isAxiosError(err)) {
        if (err.response?.status === 403 && (err.response.data as { code?: string } | undefined)?.code === 'ACCOUNT_LOCKED') {
          // Must precede the generic err.response arm below, which would otherwise blame our
          // servers for what is a deliberate lockout - and invite the immediate retry that is
          // exactly what won't work. Wording matches AuthController.Login's lockedMessage.
          setError('This account is temporarily locked after too many failed sign-in attempts. Please try again later.')
        } else if (err.response?.status === 403 && (err.response.data as { code?: string } | undefined)?.code === 'EMAIL_NOT_CONFIRMED') {
          setError('Please verify your email before signing in.')
          setNeedsVerification(true)
        } else if (err.response?.status === 401) {
          setError('Invalid email or password.')
        } else if (err.response) {
          // Backend responded but something failed server-side (e.g. it can't reach the
          // database) - don't tell the user their credentials are wrong when they aren't.
          setError('Something went wrong on our end. Please try again in a moment.')
        } else {
          // No response at all - the backend itself isn't reachable, not just its database.
          setError('Could not reach the server. Please check your connection and try again.')
        }
      } else {
        setError('Something went wrong. Please try again.')
      }
    } finally {
      setSubmitting(false)
    }
  }

  const handleResendVerification = async () => {
    setResending(true)
    try {
      await authApi.resendVerificationEmail(email)
      navigate('/verify-email', { state: { email } })
    } finally {
      setResending(false)
    }
  }

  const autofill = (account: (typeof DEV_SEED_ACCOUNTS)[number]) => {
    setEmail(account.email)
    setPassword(account.password)
    setError(null)
  }

  return (
    <>
      <PageMeta title="Login" />
      <AuthSplitLayout heading="Welcome to PSMPE Portal" subheading="Sign in to manage your plumbing service jobs.">
        {successMessage && <p className="text-sm text-success mb-4">{successMessage}</p>}

        <form onSubmit={handleSubmit} className="text-start w-full">
          <AuthTextInput
            label="Username / Email"
            type="email"
            id="email"
            placeholder="you@example.com"
            autoComplete="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />

          <AuthTextInput
            label="Password"
            type="password"
            id="password"
            placeholder="Enter your password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />

          <div className="flex justify-end -mt-2 mb-4.5">
            <Link to="/forgot-password" className="text-sm font-semibold text-primary-500">
              Forgot password?
            </Link>
          </div>

          {error && <p className="text-sm text-danger mb-4">{error}</p>}
          {needsVerification && (
            <p className="text-sm text-default-500 mb-4">
              <button type="button" onClick={handleResendVerification} disabled={resending} className="text-primary disabled:opacity-50">
                {resending ? 'Sending…' : 'Resend verification email'}
              </button>
            </p>
          )}

          <button type="submit" disabled={submitting} className="btn bg-primary text-white w-full min-h-11">
            {submitting ? 'Signing in…' : 'Sign In'}
          </button>

          {/* TODO: OAuth sign-in (Google/Apple) hidden until backend is wired up - re-enable then. */}
        </form>

        <p className="mt-6 text-center text-sm text-default-500">
          Don't have an account?{' '}
          <Link to="/register" className="font-semibold text-primary">
            Register
          </Link>
        </p>
      </AuthSplitLayout>

      {import.meta.env.DEV && (
        // Bottom-*left*, xl-only: that's exactly where the hero photo occupies the left half, so
        // the panel sits over decorative image instead of covering the form's links. Below xl the
        // form is centered over the full-bleed background and there's no safe corner for it.
        <div className="hidden xl:block fixed bottom-4 left-4 z-20 card w-80 max-w-[calc(100vw-2rem)]">
          <div className="px-5 py-3.5">
            <h6 className="text-[11px] font-semibold text-default-700 uppercase tracking-wide mb-0.5">
              Dev credential cheatsheet
            </h6>
            <p className="text-[10px] text-default-500 mb-2.5">
              Local dev only — click a role to autofill email &amp; password above.
            </p>
            <div className="flex flex-wrap gap-2">
              {DEV_SEED_ACCOUNTS.map((account) => (
                <button
                  key={account.email}
                  type="button"
                  title={`${account.email} / ${account.password}`}
                  onClick={() => autofill(account)}
                  className="px-3 py-2 rounded-lg border border-success/30 bg-success/15 hover:bg-success/25 text-default-800 text-xs font-medium text-center transition"
                >
                  {account.role}
                </button>
              ))}
            </div>
          </div>
        </div>
      )}
    </>
  )
}
