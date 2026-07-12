import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { isAxiosError } from 'axios'
import { useAuth } from '../auth/useAuth'
import { authApi } from '../api/endpoints/authApi'
import { PageMeta } from '../../integrations/template'
import logoDark from '../../integrations/template/assets/images/logo-dark.png'
import logoLight from '../../integrations/template/assets/images/logo-light.png'
import IconifyIcon from '../../integrations/template/components/shared/IconifyIcon'

type UsernameAvailability = 'idle' | 'checking' | 'available' | 'taken'

export function RegisterPage() {
  const { register } = useAuth()
  const navigate = useNavigate()

  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [usernameAvailability, setUsernameAvailability] = useState<UsernameAvailability>('idle')
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    if (debounceRef.current) {
      clearTimeout(debounceRef.current)
    }
    if (!username) {
      setUsernameAvailability('idle')
      return
    }
    setUsernameAvailability('checking')
    debounceRef.current = setTimeout(() => {
      authApi
        .isUsernameAvailable(username)
        .then((available) => setUsernameAvailability(available ? 'available' : 'taken'))
        .catch(() => setUsernameAvailability('idle'))
    }, 500)
    return () => {
      if (debounceRef.current) {
        clearTimeout(debounceRef.current)
      }
    }
  }, [username])

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)

    if (password !== confirmPassword) {
      setError('Passwords do not match.')
      return
    }

    setSubmitting(true)
    try {
      const response = await register({ email, password, displayName, username: username || undefined })
      navigate('/verify-email', { state: { email: response.email, devVerificationLink: response.devVerificationLink } })
    } catch (err) {
      if (isAxiosError(err) && err.response?.status === 409) {
        setError(err.response.data?.message ?? 'An account with this email or username already exists.')
      } else {
        setError('Something went wrong creating your account. Please try again.')
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <>
      <PageMeta title="Register" />
      <div className="relative min-h-screen w-full flex flex-col justify-center items-center py-16 md:py-10">
        <div className="card md:w-lg w-screen z-10">
          <div className="text-center px-10 py-12">
            <Link to="/" className="flex justify-center">
              <img src={logoDark} alt="logo dark" className="h-6 flex dark:hidden" width={111} />
              <img src={logoLight} alt="logo light" className="h-6 hidden dark:flex" width={111} />
            </Link>

            <div className="mt-8 text-center">
              <h4 className="mb-2.5 text-xl font-semibold text-primary">Create Your Account</h4>
              <p className="text-base text-default-500">
                Sign up with the basics - you can complete your membership application afterward from your dashboard.
              </p>
            </div>

            <form onSubmit={handleSubmit} className="text-left w-full mt-10">
              <div className="mb-4">
                <label className="block font-medium text-default-900 text-sm mb-2">Full Name</label>
                <input
                  className="form-input"
                  required
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                />
              </div>

              <div className="mb-4">
                <label className="block font-medium text-default-900 text-sm mb-2">Email</label>
                <input
                  type="email"
                  className="form-input"
                  required
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                />
              </div>

              <div className="mb-4">
                <label className="block font-medium text-default-900 text-sm mb-2">Username (optional)</label>
                <input className="form-input" value={username} onChange={(e) => setUsername(e.target.value)} />
                {usernameAvailability === 'checking' && <p className="text-xs text-default-500 mt-1">Checking availability…</p>}
                {usernameAvailability === 'available' && <p className="text-xs text-success mt-1">Username available!</p>}
                {usernameAvailability === 'taken' && <p className="text-xs text-danger mt-1">Username is already taken.</p>}
              </div>

              <div className="mb-4">
                <label className="block font-medium text-default-900 text-sm mb-2">Password</label>
                <input
                  type="password"
                  className="form-input"
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              </div>

              <div className="mb-4">
                <label className="block font-medium text-default-900 text-sm mb-2">Confirm Password</label>
                <input
                  type="password"
                  className="form-input"
                  required
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                />
              </div>

              {error && <p className="text-sm text-danger mb-4">{error}</p>}

              <p className="italic text-sm font-medium text-default-500">
                By registering you agree to the PSMPE Portal <Link to="#" className="underline">Terms of Use</Link>
              </p>

              <div className="mt-10 text-center">
                <button type="submit" disabled={submitting} className="btn bg-primary text-white w-full">
                  {submitting ? 'Creating account…' : 'Create Account'}
                </button>
              </div>

              <div className="my-9 relative text-center before:absolute before:top-2.5 before:left-0 before:border-t before:border-t-default-200 before:w-full before:h-0.5 before:right-0 before:-z-0">
                <h4 className="relative z-1 py-0.5 px-2 inline-block font-medium text-default-600 bg-card">Create Account With</h4>
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

              <p className="mt-6 text-center text-sm text-default-500">
                Already have an account?{' '}
                <Link to="/login" className="font-semibold text-primary">
                  Sign In
                </Link>
              </p>
            </form>
          </div>
        </div>

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
