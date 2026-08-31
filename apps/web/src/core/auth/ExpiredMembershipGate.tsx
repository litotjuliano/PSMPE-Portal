import { isAxiosError } from 'axios'
import { createContext, useContext, useEffect, useState } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { memberApi } from '../api/endpoints/memberApi'
import { useAuth } from './useAuth'
import { Roles } from '../types/auth'
import { MembershipStatus } from '../types/member'

interface MembershipAccessState {
  /** Fully Expired (past the grace period). */
  isExpired: boolean
  /** The member's most recently verified payment didn't include the portal-access add-on. */
  lacksPortalAccess: boolean
  /** Either condition above - used by AppMenu to hide everything but Profile. Always all-false for
   *  administrative accounts. */
  isRestricted: boolean
}

const defaultMembershipAccessState: MembershipAccessState = {
  isExpired: false,
  lacksPortalAccess: false,
  isRestricted: false,
}

const MembershipAccessContext = createContext<MembershipAccessState>(defaultMembershipAccessState)

/** The signed-in member's expiry/portal-access restriction state - see MembershipAccessState. */
// eslint-disable-next-line react-refresh/only-export-components
export const useMembershipAccess = () => useContext(MembershipAccessContext)

/**
 * Redirects a restricted member to /profile from any other route, mirroring
 * DataPrivacyConsentGate's effect-fetch/fail-open shape as its own layout route. A member is
 * restricted when fully Expired (past the grace period), lacking portal access (their most
 * recently verified payment omitted the add-on), or has no Member profile at all yet (registered
 * but never submitted an application - getMyProfile() 404s) - any one condition alone is enough.
 * Grace-period members (Status still Active) with portal access are unaffected.
 *
 * This is UX only, not the security boundary: MembershipAccessMiddleware on the backend is what
 * actually enforces all three restrictions, so a request slipping past this redirect (e.g. during
 * the brief window before the fetch resolves) still gets a 403 from the API.
 *
 * Fails open on any OTHER fetch error / while loading - same trade-off DataPrivacyConsentGate
 * already makes, since briefly showing a page to someone whose status hasn't loaded yet is far
 * better than locking everyone out on a transient 500. A 404 specifically is not treated as
 * transient - see the catch block below.
 */
export function ExpiredMembershipGate() {
  const { user } = useAuth()
  const location = useLocation()
  const [state, setState] = useState<MembershipAccessState>(defaultMembershipAccessState)

  // Mirrors MyProfilePage.tsx's isAdministrativeAccount check: staff/admin roles never have a
  // Member profile, so they can never be Expired or lack portal access.
  const isPureMember = (user?.roles ?? []).length > 0 && (user?.roles ?? []).every((role) => role === Roles.Member)

  useEffect(() => {
    if (!isPureMember) {
      setState(defaultMembershipAccessState)
      return
    }

    let cancelled = false
    memberApi
      .getMyProfile()
      .then((member) => {
        if (cancelled) return
        const isExpired = member.status === MembershipStatus.Expired
        const lacksPortalAccess = !member.hasPortalAccess
        setState({ isExpired, lacksPortalAccess, isRestricted: isExpired || lacksPortalAccess })
      })
      .catch((err) => {
        if (cancelled) return
        // A 404 here means this Member-role account has no Member row at all yet - registered but
        // never submitted an application (see MembershipAccessMiddleware's MEMBERSHIP_NOT_STARTED
        // check, which enforces this as a real restriction, not just this redirect). That's a real,
        // meaningful state, not a transient failure - restrict the same as Expired/lacking portal
        // access so the member lands on /profile, which already renders the application wizard for
        // exactly this case. Anything else (network error, 500) keeps failing open as before.
        setState(isAxiosError(err) && err.response?.status === 404 ? { ...defaultMembershipAccessState, isRestricted: true } : defaultMembershipAccessState)
      })
    return () => {
      cancelled = true
    }
  }, [isPureMember])

  return (
    <MembershipAccessContext.Provider value={state}>
      {state.isRestricted && location.pathname !== '/profile' ? <Navigate to="/profile" replace /> : <Outlet />}
    </MembershipAccessContext.Provider>
  )
}
