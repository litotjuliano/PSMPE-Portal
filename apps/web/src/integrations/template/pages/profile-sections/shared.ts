import { isAxiosError } from 'axios'
import type { Member } from '../../../../core/types/member'
import type { UpdateMyProfileRequest } from '../../../../core/api/endpoints/memberApi'

/** Same shape as MyProfilePage's/the wizard's own copy - this codebase duplicates this small
 *  helper per feature rather than sharing a single generic error module. */
export function describeError(err: unknown, fallback: string): string {
  if (isAxiosError(err)) {
    if (err.response) {
      const message = (err.response.data as { message?: string } | undefined)?.message
      return message ?? `Server error (${err.response.status}). Please try again.`
    }
    return 'Could not reach the server. Please check your connection and try again.'
  }
  return fallback
}

/**
 * Builds a full UpdateMyProfileRequest from the member's current saved values, so each profile
 * tab's Save can overlay just its own edited fields onto the whole-object upsert contract
 * MemberService.UpsertMyProfileAsync expects, instead of needing a separate partial-patch
 * endpoint. `prcIdReuploaded` defaults false - only the Personal Information section (the only
 * one that can change PrcLicenseNo) ever needs to override it to true.
 */
export function buildFullProfileRequest(member: Member, overrides: Partial<UpdateMyProfileRequest> = {}): UpdateMyProfileRequest {
  return {
    firstName: member.firstName,
    middleName: member.middleName,
    lastName: member.lastName,
    suffix: member.suffix,
    birthdate: member.birthdate,
    gender: member.gender,
    address: member.address,
    prcLicenseNo: member.prcLicenseNo,
    chapter: member.chapter,
    company: member.company,
    memberType: member.memberType,
    prcIdReuploaded: false,
    ...overrides,
  }
}
