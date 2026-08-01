import { useCallback, useEffect, useState } from 'react'
import { Outlet } from 'react-router-dom'
import { LuShieldCheck } from 'react-icons/lu'
import { authApi } from '../api/endpoints/authApi'
import { useAuth } from './useAuth'
import { DataPrivacyConsentText } from '../constants/dataPrivacyConsent'

/**
 * Blocks the app until the signed-in account holds consent at the *current* wording version.
 *
 * Sits between ProtectedRoute and AppShell as its own layout route rather than inside
 * ProtectedRoute, which is re-applied per admin route and would otherwise run this check several
 * times per navigation.
 *
 * Dormant in normal operation: everyone who registers is stamped with the current version, so the
 * prompt only appears once `DataPrivacyConsent.CurrentVersion` is bumped, plus for accounts that
 * never consented at all (seeded and admin-created). It is a prompt, not a lockout - agreeing
 * clears it immediately, and signing out is always available.
 */
export function DataPrivacyConsentGate() {
  const { logout } = useAuth()
  const [needsConsent, setNeedsConsent] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    authApi
      .getDataPrivacyConsent()
      .then((status) => {
        if (!cancelled) setNeedsConsent(status.needsConsent)
      })
      // Deliberately fails *open*. A transient 500 or dropped connection would otherwise lock
      // every user out of the whole portal, which is a far worse outcome than briefly serving
      // someone whose re-consent is outstanding - they are still prompted on the next load.
      .catch(() => {
        if (!cancelled) setNeedsConsent(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const handleAccept = useCallback(async () => {
    setSubmitting(true)
    setError(null)
    try {
      const status = await authApi.giveDataPrivacyConsent()
      setNeedsConsent(status.needsConsent)
    } catch {
      setError('Could not record your consent. Please try again in a moment.')
    } finally {
      setSubmitting(false)
    }
  }, [])

  return (
    <>
      <Outlet />

      {needsConsent && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="data-privacy-consent-heading"
          className="fixed inset-0 z-50 flex items-center justify-center bg-default-950/60 px-4 py-6 overflow-y-auto"
        >
          <div className="card w-full max-w-lg my-auto px-6 py-7 shadow-2xl shadow-primary/25 ring-1 ring-primary-100">
            <div className="flex items-center gap-2 mb-3">
              <LuShieldCheck className="size-5 text-primary shrink-0" aria-hidden="true" />
              <h2 id="data-privacy-consent-heading" className="text-lg font-semibold text-default-900">
                Updated data privacy notice
              </h2>
            </div>

            <p className="text-sm text-default-500 mb-4">
              Our data privacy notice has changed since you last agreed. Please review and accept it to continue using
              the portal.
            </p>

            <div className="text-sm text-default-500 font-medium leading-relaxed mb-5">
              <DataPrivacyConsentText />
            </div>

            {error && <p className="text-sm text-danger mb-4">{error}</p>}

            <div className="flex flex-wrap items-center justify-end gap-3">
              <button type="button" onClick={logout} className="btn min-h-11 text-default-600">
                Sign out
              </button>
              <button
                type="button"
                onClick={handleAccept}
                disabled={submitting}
                className="btn bg-primary text-white min-h-11 disabled:opacity-70"
              >
                {submitting ? 'Saving…' : 'I Agree and Continue'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
