import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { isAxiosError } from 'axios'
import { useAuth } from '../auth/useAuth'
import { authApi } from '../api/endpoints/authApi'
import { passwordErrors } from '../utils/passwordPolicy'
import { AuthSplitLayout, AuthTextInput, PageMeta } from '../../integrations/template'

type UsernameAvailability = 'idle' | 'checking' | 'available' | 'taken'

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

interface FieldErrors {
  displayName?: string
  email?: string
  password?: string
  confirmPassword?: string
  terms?: string
}

export function RegisterPage() {
  const { register } = useAuth()
  const navigate = useNavigate()

  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [termsAccepted, setTermsAccepted] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [serverErrors, setServerErrors] = useState<string[]>([])
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
    setServerErrors([])

    const nextFieldErrors: FieldErrors = {}
    if (!displayName.trim()) nextFieldErrors.displayName = 'Full name is required.'
    if (!EMAIL_PATTERN.test(email)) nextFieldErrors.email = 'Enter a valid email address.'
    const passwordIssues = passwordErrors(password)
    if (passwordIssues.length > 0) nextFieldErrors.password = passwordIssues.join(' ')
    if (password !== confirmPassword) nextFieldErrors.confirmPassword = 'Passwords do not match.'
    if (!termsAccepted) nextFieldErrors.terms = 'You must give your data privacy consent to continue.'

    setFieldErrors(nextFieldErrors)
    if (Object.keys(nextFieldErrors).length > 0) {
      return
    }

    setSubmitting(true)
    try {
      const response = await register({
        email,
        password,
        displayName,
        username: username || undefined,
        dataPrivacyConsent: termsAccepted,
      })
      navigate('/verify-email', { state: { email: response.email, devVerificationLink: response.devVerificationLink } })
    } catch (err) {
      if (isAxiosError(err) && err.response?.status === 409) {
        setError(err.response.data?.message ?? 'An account with this email or username already exists.')
      } else if (isAxiosError(err) && err.response?.status === 400 && err.response.data?.errors) {
        const messages = Object.values(err.response.data.errors as Record<string, string[]>).flat()
        setServerErrors(messages.length > 0 ? messages : ['Please check the highlighted fields and try again.'])
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
      <AuthSplitLayout heading="Create an Account" subheading="Register as a service partner or plumber.">
        <form onSubmit={handleSubmit} className="text-start w-full">
          <AuthTextInput
            label="Full Name"
            placeholder="Juan Dela Cruz"
            required
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            error={fieldErrors.displayName}
          />

          <AuthTextInput
            label="Email Address"
            type="email"
            placeholder="you@example.com"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            error={fieldErrors.email}
          />

          <div className="mb-4">
            <AuthTextInput label="Username (optional)" value={username} onChange={(e) => setUsername(e.target.value)} />
            {usernameAvailability === 'checking' && <p className="text-xs text-default-500 -mt-3">Checking availability…</p>}
            {usernameAvailability === 'available' && <p className="text-xs text-success -mt-3">Username available!</p>}
            {usernameAvailability === 'taken' && <p className="text-xs text-danger -mt-3">Username is already taken.</p>}
          </div>

          <AuthTextInput
            label="Password"
            type="password"
            placeholder="Enter your password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            error={fieldErrors.password}
          />
          <p className="text-xs text-default-500 -mt-3 mb-4">
            At least 8 characters, with an uppercase letter, a lowercase letter, and a digit.
          </p>

          <AuthTextInput
            label="Confirm Password"
            type="password"
            placeholder="Enter your confirm password"
            required
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            error={fieldErrors.confirmPassword}
          />

          {error && <p className="text-sm text-danger mb-4">{error}</p>}
          {serverErrors.length > 0 && (
            <ul className="text-sm text-danger mb-4 list-disc pl-5">
              {serverErrors.map((message) => (
                <li key={message}>{message}</li>
              ))}
            </ul>
          )}

          {/* items-start (not items-center) so the box stays on the first line of what is now a
              full consent paragraph rather than floating at its vertical middle. */}
          <div className="flex items-start gap-2 text-start mb-6 mt-3">
            <input
              type="checkbox"
              id="checkbox-terms"
              className="mt-1 size-4 shrink-0 rounded border-default-300 text-primary focus:ring focus:ring-primary/30 focus:ring-offset-0"
              checked={termsAccepted}
              onChange={(e) => setTermsAccepted(e.target.checked)}
            />
            <label className="text-sm text-default-500 font-medium select-none leading-relaxed" htmlFor="checkbox-terms">
              <span className="font-semibold text-default-900">DATA PRIVACY CONSENT:</span> I agree that my personal
              information may be collected, processed, stored, and maintained by the Association. In digital, electronic,
              and/or printed form. My personal information shall be kept confidential and used solely for legitimate
              organizational purposes in accordance with the{' '}
              <a
                href="https://privacy.gov.ph/data-privacy-act/"
                target="_blank"
                rel="noopener noreferrer"
                className="font-semibold text-primary underline"
              >
                Data Privacy Act of 2012 (Republic Act No. 10173)
              </a>
              .
            </label>
          </div>
          {fieldErrors.terms && <p className="text-xs text-danger -mt-4 mb-4">{fieldErrors.terms}</p>}

          <button type="submit" disabled={submitting} className="btn bg-primary text-white w-full min-h-11">
            {submitting ? 'Creating account…' : 'Create Account'}
          </button>

          {/* TODO: OAuth sign-up (Google/Apple) hidden until backend is wired up - re-enable then. */}
        </form>

        <p className="mt-6 text-center text-sm text-default-500">
          Already have an account?{' '}
          <Link to="/login" className="font-semibold text-primary">
            Sign In
          </Link>
        </p>
      </AuthSplitLayout>
    </>
  )
}
