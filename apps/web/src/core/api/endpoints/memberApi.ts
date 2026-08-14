import { apiClient } from '../apiClient'
import type { PagedResult } from './adminApi'
import type { Member, MembershipStatusValue } from '../../types/member'

export interface GetMembersParams {
  page?: number
  pageSize?: number
  sortBy?: 'lastName' | 'membershipNo' | 'chapter' | 'status' | 'submittedAt'
  sortDir?: 'asc' | 'desc'
  status?: MembershipStatusValue
  /** Applications with no ApprovedAt yet - distinct from status, since an approved
   *  application can still be Status.Pending (approved-but-unpaid). */
  pendingApprovalOnly?: boolean
  /** Members with a proposed PRC License No. change awaiting a decision, or whose current
   *  PRC License No. has never been reviewed at all. */
  pendingPrcVerificationOnly?: boolean
  /** Matches name, Membership No., or email - case-insensitive substring match. */
  search?: string
}

export interface CreateMemberRequest {
  userId: string
  /** Optional - an admin creating a profile may not have PSMPE's control number yet. Mandatory at approval. */
  membershipNo: string | null
  firstName: string
  middleName: string | null
  lastName: string
  suffix: string | null
  birthdate: string | null
  gender: string | null
  civilStatus: string | null
  educationLevel: string | null
  schoolName: string | null
  courseYearGraduated: string | null
  specifiedProfession: string | null
  mobileNumber: string | null
  houseNo: string | null
  street: string | null
  barangay: string | null
  cityMunicipality: string | null
  province: string | null
  zipCode: string | null
  country: string | null
  mailingHouseNo: string | null
  mailingStreet: string | null
  mailingBarangay: string | null
  mailingCityMunicipality: string | null
  mailingProvince: string | null
  mailingZipCode: string | null
  mailingCountry: string | null
  housePhone: string | null
  prcLicenseNo: string | null
  prcRegistrationDate: string | null
  prcValidUntilDate: string | null
  ptrNumber: string | null
  ptrPlaceIssued: string | null
  ptrDateIssued: string | null
  tin: string | null
  chapter: string
  chapterYear: number | null
  chapterPosition: string | null
  employmentStatus: string | null
  company: string | null
  position: string | null
  businessAddress: string | null
  yearsOfPractice: number | null
  specialization: string | null
  skills: string | null
  memberType: string
  renewalDueDate: string | null
  nationalDuesReferenceNo: string | null
}

/** No prcIdVerified field - verification is only ever set via memberApi's approve/rejectPrcVerification
 *  calls, so every decision goes through the audit trail rather than a raw toggle. */
export interface UpdateMemberRequest {
  /** Correction path for a control number mistyped at approval. Blank/omitted leaves the stored
   *  value alone - it is not a way to clear an approved member's number. */
  membershipNo?: string | null
  firstName: string
  middleName: string | null
  lastName: string
  suffix: string | null
  birthdate: string | null
  gender: string | null
  civilStatus: string | null
  educationLevel: string | null
  schoolName: string | null
  courseYearGraduated: string | null
  specifiedProfession: string | null
  mobileNumber: string | null
  houseNo: string | null
  street: string | null
  barangay: string | null
  cityMunicipality: string | null
  province: string | null
  zipCode: string | null
  country: string | null
  mailingHouseNo: string | null
  mailingStreet: string | null
  mailingBarangay: string | null
  mailingCityMunicipality: string | null
  mailingProvince: string | null
  mailingZipCode: string | null
  mailingCountry: string | null
  housePhone: string | null
  prcLicenseNo: string | null
  prcRegistrationDate: string | null
  prcValidUntilDate: string | null
  ptrNumber: string | null
  ptrPlaceIssued: string | null
  ptrDateIssued: string | null
  tin: string | null
  chapter: string
  chapterYear: number | null
  chapterPosition: string | null
  employmentStatus: string | null
  company: string | null
  position: string | null
  businessAddress: string | null
  yearsOfPractice: number | null
  specialization: string | null
  skills: string | null
  memberType: string
  status: MembershipStatusValue
  renewalDueDate: string | null
  nationalDuesReferenceNo: string | null
}

export interface UpdateMyProfileRequest {
  firstName: string
  middleName: string | null
  lastName: string
  suffix: string | null
  birthdate: string | null
  gender: string | null
  civilStatus: string | null
  educationLevel: string | null
  schoolName: string | null
  courseYearGraduated: string | null
  specifiedProfession: string | null
  mobileNumber: string | null
  houseNo: string | null
  street: string | null
  barangay: string | null
  cityMunicipality: string | null
  province: string | null
  zipCode: string | null
  country: string | null
  mailingHouseNo: string | null
  mailingStreet: string | null
  mailingBarangay: string | null
  mailingCityMunicipality: string | null
  mailingProvince: string | null
  mailingZipCode: string | null
  mailingCountry: string | null
  housePhone: string | null
  prcLicenseNo: string | null
  prcRegistrationDate: string | null
  prcValidUntilDate: string | null
  ptrNumber: string | null
  ptrPlaceIssued: string | null
  ptrDateIssued: string | null
  tin: string | null
  chapter: string
  chapterYear: number | null
  chapterPosition: string | null
  employmentStatus: string | null
  company: string | null
  position: string | null
  businessAddress: string | null
  yearsOfPractice: number | null
  specialization: string | null
  skills: string | null
  memberType: string
  /** Asserts a new RMP/PRC ID was just uploaded in this edit - required whenever prcLicenseNo,
   *  prcRegistrationDate, or prcValidUntilDate changes on an already-submitted application (see
   *  MemberService.UpsertMyProfileAsync). */
  prcIdReuploaded: boolean
}

/** Computed on demand (not part of Member) - see ProfileCompletenessDto on the backend. */
export interface ProfileCompleteness {
  percentComplete: number
  isSubmitted: boolean
  hasPrcId: boolean
  hasValidGovernmentId: boolean
  hasPhoto: boolean
  hasSignature: boolean
  hasProofOfPayment: boolean
  certificateCount: number
  hasProfessionalInfo: boolean
}

export interface MemberCertificate {
  id: string
  fileName: string
  contentType: string
  fileSizeBytes: number
  createdAt: string
}

export interface MembershipNoAvailability {
  /** The trimmed value the server actually checked - compare against what's on screen before
   *  trusting a response, since debounced requests can resolve out of order. */
  membershipNo: string
  isAvailable: boolean
}

/** Mirrors MemberStatsDto - aggregated Membership statistics for the admin dashboard.
 *  See GET /api/members/stats (Members.View-gated). */
export interface MemberStats {
  statusCounts: { pending: number; active: number; expired: number; deactivated: number }
  /** Last 12 calendar months, oldest first, zero-filled for months with no submissions. */
  registrationTrend: { year: number; month: number; count: number }[]
  /** One row per chapter, zero-filled for chapters with no members. */
  byChapter: { name: string; count: number }[]
  /** One row per member type, zero-filled for types with no members. */
  byMemberType: { name: string; count: number }[]
  actionItems: { pendingApprovals: number; pendingPrcVerification: number; renewalsDueSoon: number }
}

export const memberApi = {
  getMembers: (params: GetMembersParams = {}) =>
    apiClient.get<PagedResult<Member>>('/api/members', { params }).then((res) => res.data),

  getMemberById: (id: string) => apiClient.get<Member>(`/api/members/${id}`).then((res) => res.data),

  getStats: () => apiClient.get<MemberStats>('/api/members/stats').then((res) => res.data),

  getMyProfile: () => apiClient.get<Member>('/api/members/me').then((res) => res.data),

  createMember: (request: CreateMemberRequest) =>
    apiClient.post<Member>('/api/members', request).then((res) => res.data),

  updateMember: (id: string, request: UpdateMemberRequest) =>
    apiClient.put(`/api/members/${id}`, request).then((res) => res.data),

  updateMyProfile: (request: UpdateMyProfileRequest) =>
    apiClient.put<Member>('/api/members/me', request).then((res) => res.data),

  deleteMember: (id: string) => apiClient.delete(`/api/members/${id}`).then((res) => res.data),

  /** PSMPE's own control number, keyed in by the admin - the portal never generates one, so
   *  approval is impossible without it. 400 if blank, 409 if already in use. */
  /** Admits the member and accepts their registration payment in one server-side transaction, so
   *  there is no state where they are approved but unpaid. `payment` is sent only when the member
   *  has none on record (admin-created profile / walk-in) - supplying it otherwise is refused. */
  approveMember: (
    id: string,
    membershipNo: string,
    payment?: { amount: number; referenceNo: string | null; paidOn: string; proofStorageKey: string },
  ) => apiClient.post(`/api/members/${id}/approve`, { membershipNo, payment }).then((res) => res.data),

  /** Advisory - the approve endpoint re-checks, and the database has the final say. */
  checkMembershipNoAvailability: (value: string, excludeMemberId?: string) =>
    apiClient
      .get<MembershipNoAvailability>('/api/members/membership-no/availability', {
        params: { value, excludeMemberId },
      })
      .then((res) => res.data),

  submitMyProfile: () => apiClient.post('/api/members/me/submit').then((res) => res.data),

  approvePrcVerification: (id: string) => apiClient.post(`/api/members/${id}/prc-verification/approve`).then((res) => res.data),

  rejectPrcVerification: (id: string, reason: string) =>
    apiClient.post(`/api/members/${id}/prc-verification/reject`, { reason }).then((res) => res.data),

  getMyProfileCompleteness: () => apiClient.get<ProfileCompleteness>('/api/members/me/completeness').then((res) => res.data),

  getMemberProfileCompleteness: (id: string) =>
    apiClient.get<ProfileCompleteness>(`/api/members/${id}/completeness`).then((res) => res.data),

  getMyCertificates: () => apiClient.get<MemberCertificate[]>('/api/members/me/certificates').then((res) => res.data),

  deleteMyCertificate: (certificateId: string) =>
    apiClient.delete(`/api/members/me/certificates/${certificateId}`).then((res) => res.data),
}
