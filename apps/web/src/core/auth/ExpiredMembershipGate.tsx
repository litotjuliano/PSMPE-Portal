import { isAxiosError } from 'axios'
import { createContext, useContext, useEffect, useState } from 'react'
import { Outlet } from 'react-router-dom'
import { memberApi } from '../api/endpoints/memberApi'
import { useAuth } from './useAuth'
import { Roles } from '../types/auth'
import { MembershipStatus } from '../types/member'

interface MembershipAccessState {
  /** Fully Expired (past the grace period). */
  isExpired: boolean
  /** The member's most recently verified payment didn't include the portal-access add-on. */
  lacksPortalAccess: boolean
  /** Registered but never submitted a membership application - no Member row exists at all yet
   *  (getMyProfile() 404s). See MembershipAccessMiddleware's MEMBERSHIP_NOT_STARTED check. */
  hasNoProfile: boolean
  /** Any condition above. Doesn't gate navigation (see this file's doc comment) - consumers use it
   *  to disable a specific action (e.g. EventRegisterModal's Register button) and show a message
   *  explaining why. Always all-false for administrative accounts. */
  isRestricted: boolean
}

const defaultMembershipAccessState: MembershipAccessState = {
  isExpired: false,
  lacksPortalAccess: false,
  hasNoProfile: false,
  isRestricted: false,
}

const MembershipAccessContext = createContext<MembershipAccessState>(defaultMembershipAccessState)

/** The signed-in member's expiry/portal-access restriction state - see MembershipAccessState. */
// eslint-disable-next-line react-refresh/only-export-components
export const useMembershipAccess = () => useContext(MembershipAccessContext)

/**
 * Resolves the signed-in member's restriction state and makes it available to descendants via
 * useMembershipAccess - fully Expired (past the grace period), lacking portal access (their most
 * recently verified payment omitted the add-on), or no Member profile at all yet (registered but
 * never submitted an application - getMyProfile() 404s). Grace-period members (Status still
 * Active) with portal access are unaffected.
 *
 * Deliberately does NOT redirect or hide navigation for a restricted member - every page stays
 * reachable and every nav item stays visible (see AppMenu.tsx, which is role-filtered only).
 * Restriction shows up at the point of a specific gated action instead (e.g.
 * EventRegisterModal disabling Register with a message using this state), matching whatever the
 * backend's MembershipAccessMiddleware allowlist (`[AllowExpiredMember]`) actually permits for
 * that action - browsing is intentionally more permissive than acting.
 *
 * Fails open on any fetch error other than a 404 / while loading - same trade-off
 * DataPrivacyConsentGate already makes, since briefly showing a page to someone whose status
 * hasn't loaded yet is far better than treating everyone as restricted on a transient 500. A 404
 * specifically means "no Member row" and is treated as the real, meaningful hasNoProfile state.
 */
export function ExpiredMembershipGate() {
  const { user } = useAuth()
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
        setState({ isExpired, lacksPortalAccess, hasNoProfile: false, isRestricted: isExpired || lacksPortalAccess })
      })
      .catch((err) => {
        if (cancelled) return
        // A 404 here means this Member-role account has no Member row at all yet - registered but
        // never submitted an application (see MembershipAccessMiddleware's MEMBERSHIP_NOT_STARTED
        // check, which enforces this as a real restriction on gated actions, not just this state).
        // Anything else (network error, 500) keeps failing open as before.
        const hasNoProfile = isAxiosError(err) && err.response?.status === 404
        setState(hasNoProfile ? { ...defaultMembershipAccessState, hasNoProfile: true, isRestricted: true } : defaultMembershipAccessState)
      })
    return () => {
      cancelled = true
    }
  }, [isPureMember])

  return (
    <MembershipAccessContext.Provider value={state}>
      <Outlet />
    </MembershipAccessContext.Provider>
  )
}
