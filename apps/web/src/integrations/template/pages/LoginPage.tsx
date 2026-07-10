import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import logoDark from '../assets/images/logo-dark.png'
import logoLight from '../assets/images/logo-light.png'
import IconifyIcon from '../components/shared/IconifyIcon'
import PageMeta from '../components/shared/PageMeta'
import { useAuth } from '../../../core/auth/useAuth'

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

export const LoginPage = () => {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setSubmitting(true)
    setError(null)
    try {
      await login({ email, password })
      navigate('/')
    } catch {
      setError('Invalid email or password.')
    } finally {
      setSubmitting(false)
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
      <div className="relative min-h-screen w-full flex flex-col justify-center items-center py-16 md:py-10">
        <div className="card md:w-lg w-screen z-10">
          <div className="text-center px-10 py-12">
            <Link to="/" className="flex justify-center">
              <img src={logoDark} alt="logo dark" className="h-6 flex dark:hidden" width={111} />
              <img src={logoLight} alt="logo light" className="h-6 hidden dark:flex" width={111} />
            </Link>

            <div className="mt-8 text-center">
              <h4 className="mb-2.5 text-xl font-semibold text-primary">Welcome Back!</h4>
              <p className="text-base text-default-500">Sign in to continue to PSMPE Portal.</p>
            </div>

            <form onSubmit={handleSubmit} className="text-left w-full mt-10">
              <div className="mb-4">
                <label htmlFor="email" className="block font-medium text-default-900 text-sm mb-2">
                  Email
                </label>
                <input
                  type="email"
                  id="email"
                  className="form-input"
                  placeholder="Enter your email"
                  autoComplete="email"
                  required
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                />
              </div>

              <div className="mb-4">
                <label htmlFor="password" className="block font-medium text-default-900 text-sm mb-2">
                  Password
                </label>
                <input
                  type="password"
                  id="password"
                  className="form-input"
                  placeholder="Enter Password"
                  autoComplete="current-password"
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              </div>

              {error && <p className="text-sm text-danger mb-4">{error}</p>}

              <div className="mt-10 text-center">
                <button type="submit" disabled={submitting} className="btn bg-primary text-white w-full">
                  {submitting ? 'Signing in…' : 'Sign In'}
                </button>
              </div>

              <div className="my-9 relative text-center before:absolute before:top-2.5 before:left-0 before:border-t before:border-t-default-200 before:w-full before:h-0.5 before:right-0 before:-z-0">
                <h4 className="relative z-1 py-0.5 px-2 inline-block font-medium text-default-600 bg-card">Sign In With</h4>
              </div>

              <div className="flex w-full justify-center items-center gap-2">
                {/* TODO: no OAuth backend wired up yet - these are visual placeholders. */}
                <Link to="#" className="btn border border-default-200 flex-grow hover:bg-default-150 shadow-sm hover:text-default-800">
                  <IconifyIcon icon={'logos:google-icon'} />
                  Use Google
                </Link>

                <Link to="#" className="btn border border-default-200 flex-grow hover:bg-default-150 shadow-sm hover:text-default-800">
                  <IconifyIcon icon={'logos:apple'} className="text-mono" />
                  Use Apple
                </Link>
              </div>
            </form>
          </div>
        </div>

        {import.meta.env.DEV && (
          <div className="card md:w-lg w-screen z-10 mt-3">
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

        <div className="absolute inset-0 overflow-hidden">
          <svg aria-hidden="true" className="absolute inset-0 size-full fill-black/2 stroke-black/5 dark:fill-white/2.5 dark:stroke-white/2.5">
            <defs>
              <pattern id="authPattern" width="56" height="56" patternUnits="userSpaceOnUse" x="50%" y="16">
                <path d="M.5 56V.5H72" fill="none"></path>
              </pattern>
            </defs>
            <rect width="100%" height="100%" strokeWidth="0" fill="url(#authPattern)"></rect>
          </svg>
        </div>
      </div>
    </>
  )
}
