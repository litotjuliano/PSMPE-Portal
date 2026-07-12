import { useEffect, useState } from 'react'
import { isAxiosError } from 'axios'
import { memberApi } from '../api/endpoints/memberApi'
import { useAuth } from '../auth/useAuth'
import { MemberTypes } from '../types/member'
import type { Member } from '../types/member'
import {
  MembershipApplicationWizardCard,
  type MembershipApplicationState,
  MyProfileTabsCard,
  PageBreadcrumb,
  PageMeta,
} from '../../integrations/template'

/**
 * Best-effort split of the account's single "Full Name" field into structured name parts, so the
 * wizard's Personal Information step doesn't make the applicant re-type what they already
 * entered at sign-up. Suffix (Jr./III/etc.) is never guessed - too unreliable to detect - so it
 * always starts blank; the applicant can still edit anything this guesses wrong.
 */
function splitDisplayName(displayName: string): { firstName: string; middleName: string; lastName: string } {
  const words = displayName.trim().split(/\s+/).filter(Boolean)
  if (words.length === 0) {
    return { firstName: '', middleName: '', lastName: '' }
  }
  if (words.length === 1) {
    return { firstName: words[0], middleName: '', lastName: '' }
  }
  if (words.length === 2) {
    return { firstName: words[0], middleName: '', lastName: words[1] }
  }
  return { firstName: words[0], middleName: words.slice(1, -1).join(' '), lastName: words[words.length - 1] }
}

function buildEmptyWizardState(displayName: string): MembershipApplicationState {
  return {
    ...splitDisplayName(displayName),
    suffix: '',
    birthdate: '',
    gender: '',
    chapter: '',
    memberType: MemberTypes.Regular,
    prcLicenseNo: '',
    address: '',
    company: '',
    agreedToTerms: false,
  }
}

function toWizardState(member: Member): MembershipApplicationState {
  return {
    firstName: member.firstName,
    middleName: member.middleName ?? '',
    lastName: member.lastName,
    suffix: member.suffix ?? '',
    birthdate: member.birthdate ?? '',
    gender: member.gender ?? '',
    chapter: member.chapter,
    memberType: member.memberType || MemberTypes.Regular,
    prcLicenseNo: member.prcLicenseNo ?? '',
    address: member.address ?? '',
    company: member.company ?? '',
    agreedToTerms: false,
  }
}

/** Personal Info's required fields are all that's needed to move past step 0 on resume. */
function hasCompletedPersonalInfo(member: Member): boolean {
  return Boolean(member.firstName && member.lastName && member.chapter && member.memberType)
}

/**
 * Surfaces the actual cause instead of a generic "something went wrong" - distinguishes a real
 * field-validation message from the server, an unrelated server error, and the backend being
 * unreachable entirely (e.g. Postgres/API not running), same as LoginPage's error handling.
 */
function describeError(err: unknown, fallback: string): string {
  if (isAxiosError(err)) {
    if (err.response) {
      const message = (err.response.data as { message?: string } | undefined)?.message
      return message ?? `Server error (${err.response.status}). Please try again.`
    }
    return 'Could not reach the server. Please check your connection and try again.'
  }
  return fallback
}

export function MyProfilePage() {
  const { user } = useAuth()
  const [existing, setExisting] = useState<Member | null>(null)
  const [wizardState, setWizardState] = useState<MembershipApplicationState>(() => buildEmptyWizardState(user?.displayName ?? ''))
  const [wizardStep, setWizardStep] = useState(0)
  // How far the applicant has gotten this session - drives which stepper circles are clickable
  // (completed/reached) vs. disabled (future). Client-side only, not persisted: a page reload
  // resets it back to the same resume heuristic used for wizardStep below. No typed data is ever
  // lost by this reset - everything is already saved server-side via saveDraft on every
  // Next/Back/stepper-click, this only affects which circles are clickable right after a reload.
  const [maxStepReached, setMaxStepReached] = useState(0)
  const [wizardError, setWizardError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  // Guards Back/stepper-click saves the same way `submitting` guards Submit, so rapid clicks
  // can't fire overlapping saveDraft calls.
  const [navigating, setNavigating] = useState(false)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    memberApi
      .getMyProfile()
      .then((member) => {
        setExisting(member)
        setWizardState(toWizardState(member))
        const initialStep = hasCompletedPersonalInfo(member) ? 1 : 0
        setWizardStep(initialStep)
        setMaxStepReached(initialStep)
      })
      .catch(() => {
        // No profile yet - stay on the empty wizard, starting at step 0.
        setExisting(null)
      })
      .finally(() => setLoading(false))
  }, [])

  const handleWizardChange = <K extends keyof MembershipApplicationState>(field: K, value: MembershipApplicationState[K]) => {
    setWizardState((current) => ({ ...current, [field]: value }))
  }

  const saveDraft = () =>
    memberApi.updateMyProfile({
      firstName: wizardState.firstName,
      middleName: wizardState.middleName || null,
      lastName: wizardState.lastName,
      suffix: wizardState.suffix || null,
      birthdate: wizardState.birthdate || null,
      gender: wizardState.gender || null,
      address: wizardState.address || null,
      prcLicenseNo: wizardState.prcLicenseNo || null,
      chapter: wizardState.chapter,
      company: wizardState.company || null,
      memberType: wizardState.memberType,
      // The wizard only ever runs pre-submission, where PRC License No. isn't locked yet - no
      // re-upload proof is required at this stage (see MemberService.UpsertMyProfileAsync).
      prcIdReuploaded: false,
    })

  const handleWizardNext = async () => {
    setWizardError(null)
    setNavigating(true)
    try {
      const saved = await saveDraft()
      setExisting(saved)
      // Editing a previously-completed step (jumped to via the stepper) returns to the furthest
      // step already reached, rather than just advancing one step past wherever we started -
      // otherwise fixing step 1 from step 4 would strand the applicant on step 2.
      const next = wizardStep < maxStepReached ? maxStepReached : Math.min(wizardStep + 1, 3)
      setWizardStep(next)
      setMaxStepReached((current) => Math.max(current, next))
    } catch (err) {
      setWizardError(describeError(err, 'Could not save your progress. Please try again.'))
    } finally {
      setNavigating(false)
    }
  }

  const handleWizardBack = async () => {
    setWizardError(null)
    setNavigating(true)
    try {
      // Saves before stepping back so unsaved edits on the current step are never dropped if the
      // applicant then closes the tab, same guarantee Next already provides.
      const saved = await saveDraft()
      setExisting(saved)
      setWizardStep((current) => Math.max(current - 1, 0))
    } catch (err) {
      setWizardError(describeError(err, 'Could not save your progress. Please try again.'))
    } finally {
      setNavigating(false)
    }
  }

  const handleStepClick = async (target: number) => {
    if (target === wizardStep || target > maxStepReached) return
    setWizardError(null)
    setNavigating(true)
    try {
      const saved = await saveDraft()
      setExisting(saved)
      setWizardStep(target)
    } catch (err) {
      setWizardError(describeError(err, 'Could not save your progress. Please try again.'))
    } finally {
      setNavigating(false)
    }
  }

  async function handleWizardSubmit() {
    setWizardError(null)
    setSubmitting(true)
    try {
      await saveDraft()
      await memberApi.submitMyProfile()
      const updated = await memberApi.getMyProfile()
      setExisting(updated)
    } catch (err) {
      setWizardError(describeError(err, 'Could not submit your application. Please check the earlier steps and try again.'))
    } finally {
      setSubmitting(false)
    }
  }

  const isDraft = existing === null || existing.submittedAt === null

  return (
    <>
      <PageMeta title="My Profile" />
      <main>
        <PageBreadcrumb title="My Profile" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : isDraft ? (
          <MembershipApplicationWizardCard
            step={wizardStep}
            maxStepReached={maxStepReached}
            state={wizardState}
            onChange={handleWizardChange}
            onNext={handleWizardNext}
            onBack={handleWizardBack}
            onStepClick={handleStepClick}
            onSubmit={handleWizardSubmit}
            accountEmail={user?.email ?? ''}
            accountDisplayName={user?.displayName ?? ''}
            error={wizardError}
            submitting={submitting}
            navigating={navigating}
          />
        ) : (
          <MyProfileTabsCard existing={existing as Member} accountDisplayName={user?.displayName ?? ''} onUpdated={setExisting} />
        )}
      </main>
    </>
  )
}
