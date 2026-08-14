import { createContext, useContext, useEffect, useState } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { memberApi } from '../api/endpoints/memberApi'
import { useAuth } from './useAuth'
import { Roles } from '../types/auth'
import { MembershipStatus } from '../types/member'

const MembershipAccessContext = createContext(false)

/** Whether the signed-in member is fully Expired (past the grace period) - used by AppMenu to hide
 *  everything but Profile. Always false for administrative accounts. */
// eslint-disable-next-line react-refresh/only-export-components
export const useMembershipAccess = () => useContext(MembershipAccessContext)

/**
 * Redirects a fully-Expired member to /profile from any other route, mirroring
 * DataPrivacyConsentGate's effect-fetch/fail-open shape as its own layout route. Grace-period
 * members (Status still Active) are unaffected - only Status === 'Expired' triggers this.
 *
 * This is UX only, not the security boundary: MembershipAccessMiddleware on the backend is what
 * actually enforces the restriction, so a request slipping past this redirect (e.g. during the
 * brief window before the fetch resolves) still gets a 403 from the API.
 *
 * Fails open on fetch error / while loading - same trade-off DataPrivacyConsentGate already makes,
 * since briefly showing a page to someone whose expiry status hasn't loaded yet is far better than
 * locking everyone out on a transient 500.
 */
export function ExpiredMembershipGate() {
  const { user } = useAuth()
  const location = useLocation()
  const [isExpired, setIsExpired] = useState(false)

  // Mirrors MyProfilePage.tsx's isAdministrativeAccount check: staff/admin roles never have a
  // Member profile, so they can never be Expired.
  const isPureMember = (user?.roles ?? []).length > 0 && (user?.roles ?? []).every((role) => role === Roles.Member)

  useEffect(() => {
    if (!isPureMember) {
      setIsExpired(false)
      return
    }

    let cancelled = false
    memberApi
      .getMyProfile()
      .then((member) => {
        if (!cancelled) setIsExpired(member.status === MembershipStatus.Expired)
      })
      .catch(() => {
        if (!cancelled) setIsExpired(false)
      })
    return () => {
      cancelled = true
    }
  }, [isPureMember])

  return (
    <MembershipAccessContext.Provider value={isExpired}>
      {isExpired && location.pathname !== '/profile' ? <Navigate to="/profile" replace /> : <Outlet />}
    </MembershipAccessContext.Provider>
  )
}
