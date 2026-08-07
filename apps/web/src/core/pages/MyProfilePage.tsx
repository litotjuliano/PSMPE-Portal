import { useCallback, useEffect, useRef, useState } from 'react'
import { memberApi } from '../api/endpoints/memberApi'
import { uploadApi } from '../api/endpoints/uploadApi'
import { useAuth } from '../auth/useAuth'
import { Roles } from '../types/auth'
import { MemberTypes } from '../types/member'
import type { Member } from '../types/member'
import { AccountSection } from './AccountSection'
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
    educationLevel: '',
    schoolName: '',
    courseYearGraduated: '',
    specifiedProfession: '',
    prcLicenseNo: '',
    prcRegistrationDate: '',
    prcValidUntilDate: '',
    ptrNumber: '',
    tin: '',
    company: '',
    mobileNumber: '',
    houseNo: '',
    street: '',
    barangay: '',
    cityMunicipality: '',
    province: '',
    zipCode: '',
    mailingSameAsResidence: true,
    mailingHouseNo: '',
    mailingStreet: '',
    mailingBarangay: '',
    mailingCityMunicipality: '',
    mailingProvince: '',
    mailingZipCode: '',
    housePhone: '',
    website: '',
    facebookUrl: '',
    linkedInUrl: '',
    xUrl: '',
    instagramUrl: '',
    agreedToTerms: false,
    dataPrivacyConsent: false,
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
    educationLevel: member.educationLevel ?? '',
    schoolName: member.schoolName ?? '',
    courseYearGraduated: member.courseYearGraduated ?? '',
    specifiedProfession: member.specifiedProfession ?? '',
    prcLicenseNo: member.prcLicenseNo ?? '',
    prcRegistrationDate: member.prcRegistrationDate ?? '',
    prcValidUntilDate: member.prcValidUntilDate ?? '',
    ptrNumber: member.ptrNumber ?? '',
    tin: member.tin ?? '',
    company: member.company ?? '',
    mobileNumber: member.mobileNumber ?? '',
    houseNo: member.houseNo ?? '',
    street: member.street ?? '',
    barangay: member.barangay ?? '',
    cityMunicipality: member.cityMunicipality ?? '',
    province: member.province ?? '',
    zipCode: member.zipCode ?? '',
    // Resuming a draft always shows the mailing fields explicitly (not the "same as residence"
    // shorthand) - there's no stored flag to know if they were originally mirrored or typed in.
    mailingSameAsResidence: false,
    mailingHouseNo: member.mailingHouseNo ?? '',
    mailingStreet: member.mailingStreet ?? '',
    mailingBarangay: member.mailingBarangay ?? '',
    mailingCityMunicipality: member.mailingCityMunicipality ?? '',
    mailingProvince: member.mailingProvince ?? '',
    mailingZipCode: member.mailingZipCode ?? '',
    housePhone: member.housePhone ?? '',
    website: member.website ?? '',
    facebookUrl: member.facebookUrl ?? '',
    linkedInUrl: member.linkedInUrl ?? '',
    xUrl: member.xUrl ?? '',
    instagramUrl: member.instagramUrl ?? '',
    agreedToTerms: false,
    dataPrivacyConsent: false,
  }
}

/** Step 0 (Personal Information)'s required fields are all that's needed to move past it on
 *  resume - kept in sync with the wizard's Step 1 field set. */
function hasCompletedPersonalInfo(member: Member): boolean {
  return Boolean(
    member.firstName &&
      member.lastName &&
      member.chapter &&
      member.memberType &&
      member.birthdate &&
      member.gender &&
      member.civilStatus &&
      member.educationLevel &&
      member.schoolName &&
      member.courseYearGraduated &&
      member.specifiedProfession &&
      member.prcLicenseNo &&
      member.prcRegistrationDate &&
      member.prcValidUntilDate,
  )
}

/** Step 1 (Contact Information)'s required fields - kept in sync with the wizard's Step 2 field
 *  set. House No. is deliberately not required (see MemberService.SubmitMyProfileAsync). */
function hasCompletedContactInfo(member: Member): boolean {
  return Boolean(member.mobileNumber && member.street && member.barangay && member.cityMunicipality && member.province && member.zipCode)
}

/** Step 2 (Additional Information)'s required field - kept in sync with the wizard's Step 3 field
 *  set. Step 3 (Payment Details) has no required Member field of its own (Proof of Payment is an
 *  upload, and the terms/consent checkboxes are never persisted), so it's never a distinct gate:
 *  once Additional Information is complete, there's nothing further to check for resume purposes. */
function hasCompletedAdditionalInfo(member: Member): boolean {
  return Boolean(member.ptrNumber)
}

/** How far into the 4-step wizard (0-3) an in-progress draft has already gotten, each step
 *  building on the previous - same shallow field-based approach hasCompletedPersonalInfo already
 *  used, not an upload-existence check (consistent with today's precedent). */
function furthestStepReached(member: Member): number {
  if (!hasCompletedPersonalInfo(member)) return 0
  if (!hasCompletedContactInfo(member)) return 1
  if (!hasCompletedAdditionalInfo(member)) return 2
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

  // The photo lives here rather than in each consumer: the summary card displays it and the
  // Personal Information tab replaces it, and when they each owned a copy the page fetched the
  // same blob twice and showed two avatars that could disagree after an upload.
  const [photoUrl, setPhotoUrl] = useState<string | null>(null)
  // Held in a ref so cleanup revokes the URL actually on screen, not a stale capture.
  const photoObjectUrlRef = useRef<string | null>(null)

  const showPhoto = useCallback((next: string | null) => {
    if (photoObjectUrlRef.current) URL.revokeObjectURL(photoObjectUrlRef.current)
    photoObjectUrlRef.current = next
    setPhotoUrl(next)
  }, [])

  useEffect(() => {
    let cancelled = false
    uploadApi
      .fetchMyPhotoUrl()
      .then((result) => {
        if (cancelled) {
          if (result) URL.revokeObjectURL(result.url)
          return
        }
        showPhoto(result?.url ?? null)
      })
      .catch(() => {
        // fetchMyPhotoUrl maps 404 to null, so a throw here is a real fault - but a missing avatar
        // must never block the rest of the profile from rendering.
      })
    return () => {
      cancelled = true
      if (photoObjectUrlRef.current) {
        URL.revokeObjectURL(photoObjectUrlRef.current)
        photoObjectUrlRef.current = null
      }
    }
  }, [showPhoto])

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
  const saveDraft = () => {
    // "Same as Residence Address" is a client-only convenience - there's no stored flag, the
    // mailing fields are just populated with the residence values at save time.
    const mailing = wizardState.mailingSameAsResidence
      ? {
          mailingHouseNo: wizardState.houseNo || null,
          mailingStreet: wizardState.street || null,
          mailingBarangay: wizardState.barangay || null,
          mailingCityMunicipality: wizardState.cityMunicipality || null,
          mailingProvince: wizardState.province || null,
          mailingZipCode: wizardState.zipCode || null,
        }
      : {
          mailingHouseNo: wizardState.mailingHouseNo || null,
          mailingStreet: wizardState.mailingStreet || null,
          mailingBarangay: wizardState.mailingBarangay || null,
          mailingCityMunicipality: wizardState.mailingCityMunicipality || null,
          mailingProvince: wizardState.mailingProvince || null,
          mailingZipCode: wizardState.mailingZipCode || null,
        }

    return memberApi.updateMyProfile({
      firstName: wizardState.firstName,
      middleName: wizardState.middleName || null,
      lastName: wizardState.lastName,
      suffix: wizardState.suffix || null,
      birthdate: wizardState.birthdate || null,
      gender: wizardState.gender || null,
      civilStatus: wizardState.civilStatus || null,
      educationLevel: wizardState.educationLevel || null,
      schoolName: wizardState.schoolName || null,
      courseYearGraduated: wizardState.courseYearGraduated || null,
      specifiedProfession: wizardState.specifiedProfession || null,
      mobileNumber: wizardState.mobileNumber || null,
      houseNo: wizardState.houseNo || null,
      street: wizardState.street || null,
      barangay: wizardState.barangay || null,
      cityMunicipality: wizardState.cityMunicipality || null,
      province: wizardState.province || null,
      zipCode: wizardState.zipCode || null,
      ...mailing,
      housePhone: wizardState.housePhone || null,
      website: wizardState.website || null,
      facebookUrl: wizardState.facebookUrl || null,
      linkedInUrl: wizardState.linkedInUrl || null,
      xUrl: wizardState.xUrl || null,
      instagramUrl: wizardState.instagramUrl || null,
      prcLicenseNo: wizardState.prcLicenseNo || null,
      prcRegistrationDate: wizardState.prcRegistrationDate || null,
      prcValidUntilDate: wizardState.prcValidUntilDate || null,
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
  }

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

  // Mirrors MembersController.IsSystemAccountAsync (any role other than Member). Such accounts
  // have no Member row by design, so PUT /api/members/me returns 403 ADMIN_ACCOUNT_NO_PROFILE -
  // showing them the wizard offered a save that could never succeed. The `existing === null`
  // half matters: it keeps a genuine member who was later granted a staff role on their own
  // profile rather than swapping it for the admin view.
  const isAdministrativeAccount = (user?.roles ?? []).some((role) => role !== Roles.Member)
  const hasNoMembershipProfile = existing === null

  return (
    <>
      <PageMeta title="My Profile" />
      <main>
        <PageBreadcrumb title="My Profile" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : isAdministrativeAccount && hasNoMembershipProfile ? (
          // Administrative accounts have no membership application at all, so the account section
          // is the whole page rather than an addition to it.
          <AccountSection />
        ) : (
          <div className="flex flex-col gap-4">
            {isDraft ? (
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
              <MyProfileTabsCard
                existing={existing as Member}
                onUpdated={setExisting}
                photoUrl={photoUrl}
                onPhotoChanged={showPhoto}
              />
            )}
            {/* No standalone AccountSection here any more for members - Display Name and Change
                Password now live on the Account & Security tab inside MyProfileTabsCard, and photo
                comes from ProfileRail. This second rendering was fully redundant once both moved
                in; AccountSection is still used as-is for administrative accounts above, which
                have no Member row and so no tabs to hold this content. */}
          </div>
        )}
      </main>
    </>
  )
}
