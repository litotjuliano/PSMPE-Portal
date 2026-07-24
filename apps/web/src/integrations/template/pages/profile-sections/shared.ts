import type { Member } from '../../../../core/types/member'
import type { UpdateMyProfileRequest } from '../../../../core/api/endpoints/memberApi'

export { describeError } from '../../../../core/utils/apiError'

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
    civilStatus: member.civilStatus,
    address: member.address,
    mobileNumber: member.mobileNumber,
    housePhone: member.housePhone,
    website: member.website,
    facebookUrl: member.facebookUrl,
    linkedInUrl: member.linkedInUrl,
    xUrl: member.xUrl,
    instagramUrl: member.instagramUrl,
    prcLicenseNo: member.prcLicenseNo,
    ptrNumber: member.ptrNumber,
    tin: member.tin,
    chapter: member.chapter,
    employmentStatus: member.employmentStatus,
    company: member.company,
    position: member.position,
    businessAddress: member.businessAddress,
    yearsOfPractice: member.yearsOfPractice,
    specialization: member.specialization,
    skills: member.skills,
    memberType: member.memberType,
    prcIdReuploaded: false,
    ...overrides,
  }
}
