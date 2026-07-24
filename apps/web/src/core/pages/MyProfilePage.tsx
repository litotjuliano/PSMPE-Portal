import { useEffect, useState } from 'react'
import { memberApi } from '../api/endpoints/memberApi'
import { useAuth } from '../auth/useAuth'
import { MemberTypes } from '../types/member'
import type { Member } from '../types/member'
import { describeError } from '../utils/apiError'
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
    civilStatus: '',
    chapter: '',
    memberType: MemberTypes.Regular,
    prcLicenseNo: '',
    ptrNumber: '',
    tin: '',
    company: '',
    address: '',
    mobileNumber: '',
    housePhone: '',
    website: '',
    facebookUrl: '',
    linkedInUrl: '',
    xUrl: '',
    instagramUrl: '',
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
    civilStatus: member.civilStatus ?? '',
    chapter: member.chapter,
    memberType: member.memberType || MemberTypes.Regular,
    prcLicenseNo: member.prcLicenseNo ?? '',
    ptrNumber: member.ptrNumber ?? '',
    tin: member.tin ?? '',
    company: member.company ?? '',
    address: member.address ?? '',
    mobileNumber: member.mobileNumber ?? '',
    housePhone: member.housePhone ?? '',
    website: member.website ?? '',
    facebookUrl: member.facebookUrl ?? '',
    linkedInUrl: member.linkedInUrl ?? '',
    xUrl: member.xUrl ?? '',
    instagramUrl: member.instagramUrl ?? '',
    agreedToTerms: false,
  }
}

/** Step 0 (Personal Information)'s required fields are all that's needed to move past it on
 *  resume - kept in sync with the wizard's Step 1 field set (now including PRC License No.,
 *  which moved into this step). */
function hasCompletedPersonalInfo(member: Member): boolean {
  return Boolean(
    member.firstName &&
      member.lastName &&
      member.chapter &&
      member.memberType &&
      member.birthdate &&
      member.gender &&
      member.civilStatus &&
      member.prcLicenseNo,
  )
}

/** Step 1 (Contact Information)'s required fields - kept in sync with the wizard's Step 2 field
 *  set. */
function hasCompletedContactInfo(member: Member): boolean {
  return Boolean(member.address && member.mobileNumber)
}

/** Step 3 (Additional Information)'s required field - kept in sync with the wizard's Step 4
 *  field set. Step 2 (Account Information) has no required fields of its own, so it's never a
 *  distinct gate: once Contact Information is complete, Account Information is too. */
function hasCompletedAdditionalInfo(member: Member): boolean {
  return Boolean(member.ptrNumber)
}

/** How far into the 4-step wizard (0-3) an in-progress draft has already gotten, each step
 *  building on the previous - same shallow field-based approach hasCompletedPersonalInfo already
 *  used, not an upload-existence check (consistent with today's precedent). */
function furthestStepReached(member: Member): number {
  if (!hasCompletedPersonalInfo(member)) return 0
  if (!hasCompletedContactInfo(member)) return 1
  if (!hasCompletedAdditionalInfo(member)) return 3
  return 3
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
        const initialStep = furthestStepReached(member)
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

  // Employment Status/Position/Business Address/Years of Practice/Specialization/Skills are
  // entirely post-approval (see Additional Information's Professional half, MyProfileTabsCard)
  // and never edited by this wizard - passed through unchanged from whatever's already saved so a
  // draft save here can never clobber them (see the "existing draft data is preserved"
  // requirement). Company is the exception - it's wizard-native, same as PrcLicenseNo/PtrNumber/Tin.
  const saveDraft = () =>
    memberApi.updateMyProfile({
      firstName: wizardState.firstName,
      middleName: wizardState.middleName || null,
      lastName: wizardState.lastName,
      suffix: wizardState.suffix || null,
      birthdate: wizardState.birthdate || null,
      gender: wizardState.gender || null,
      civilStatus: wizardState.civilStatus || null,
      address: wizardState.address || null,
      mobileNumber: wizardState.mobileNumber || null,
      housePhone: wizardState.housePhone || null,
      website: wizardState.website || null,
      facebookUrl: wizardState.facebookUrl || null,
      linkedInUrl: wizardState.linkedInUrl || null,
      xUrl: wizardState.xUrl || null,
      instagramUrl: wizardState.instagramUrl || null,
      prcLicenseNo: wizardState.prcLicenseNo || null,
      ptrNumber: wizardState.ptrNumber || null,
      tin: wizardState.tin || null,
      company: wizardState.company || null,
      chapter: wizardState.chapter,
      employmentStatus: existing?.employmentStatus ?? null,
      position: existing?.position ?? null,
      businessAddress: existing?.businessAddress ?? null,
      yearsOfPractice: existing?.yearsOfPractice ?? null,
      specialization: existing?.specialization ?? null,
      skills: existing?.skills ?? null,
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
      // Wizard has 4 steps (indices 0-3) - see MembershipApplicationWizardCard's `steps` array.
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
            error={wizardError}
            submitting={submitting}
            navigating={navigating}
          />
        ) : (
          <MyProfileTabsCard existing={existing as Member} onUpdated={setExisting} />
        )}
      </main>
    </>
  )
}
