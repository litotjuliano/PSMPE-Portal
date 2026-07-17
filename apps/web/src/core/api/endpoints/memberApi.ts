import { apiClient } from '../apiClient'
import type { PagedResult } from './adminApi'
import type { Member, MembershipStatusValue } from '../../types/member'

export interface GetMembersParams {
  page?: number
  pageSize?: number
  sortBy?: 'lastName' | 'membershipNo' | 'chapter' | 'status'
  sortDir?: 'asc' | 'desc'
  status?: MembershipStatusValue
  /** Applications with no ApprovedAt yet - distinct from status, since an approved
   *  application can still be Status.Pending (approved-but-unpaid). */
  pendingApprovalOnly?: boolean
  /** Members with a proposed PRC License No. change awaiting a decision, or whose current
   *  PRC License No. has never been reviewed at all. */
  pendingPrcVerificationOnly?: boolean
}

export interface CreateMemberRequest {
  userId: string
  membershipNo: string
  firstName: string
  middleName: string | null
  lastName: string
  suffix: string | null
  birthdate: string | null
  gender: string | null
  civilStatus: string | null
  address: string | null
  mobileNumber: string | null
  housePhone: string | null
  website: string | null
  facebookUrl: string | null
  linkedInUrl: string | null
  xUrl: string | null
  instagramUrl: string | null
  prcLicenseNo: string | null
  ptrNumber: string | null
  tin: string | null
  chapter: string
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
  firstName: string
  middleName: string | null
  lastName: string
  suffix: string | null
  birthdate: string | null
  gender: string | null
  civilStatus: string | null
  address: string | null
  mobileNumber: string | null
  housePhone: string | null
  website: string | null
  facebookUrl: string | null
  linkedInUrl: string | null
  xUrl: string | null
  instagramUrl: string | null
  prcLicenseNo: string | null
  ptrNumber: string | null
  tin: string | null
  chapter: string
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
  address: string | null
  mobileNumber: string | null
  housePhone: string | null
  website: string | null
  facebookUrl: string | null
  linkedInUrl: string | null
  xUrl: string | null
  instagramUrl: string | null
  prcLicenseNo: string | null
  ptrNumber: string | null
  tin: string | null
  chapter: string
  employmentStatus: string | null
  company: string | null
  position: string | null
  businessAddress: string | null
  yearsOfPractice: number | null
  specialization: string | null
  skills: string | null
  memberType: string
  /** Asserts a new PRC ID was just uploaded in this edit - required whenever prcLicenseNo changes
   *  on an already-submitted application (see MemberService.UpsertMyProfileAsync). */
  prcIdReuploaded: boolean
}

/** Computed on demand (not part of Member) - see ProfileCompletenessDto on the backend. */
export interface ProfileCompleteness {
  percentComplete: number
  isSubmitted: boolean
  hasPrcId: boolean
  hasValidGovernmentId: boolean
  hasFormalPhoto: boolean
  hasSignature: boolean
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

export const memberApi = {
  getMembers: (params: GetMembersParams = {}) =>
    apiClient.get<PagedResult<Member>>('/api/members', { params }).then((res) => res.data),

  getMemberById: (id: string) => apiClient.get<Member>(`/api/members/${id}`).then((res) => res.data),

  getMyProfile: () => apiClient.get<Member>('/api/members/me').then((res) => res.data),

  createMember: (request: CreateMemberRequest) =>
    apiClient.post<Member>('/api/members', request).then((res) => res.data),

  updateMember: (id: string, request: UpdateMemberRequest) =>
    apiClient.put(`/api/members/${id}`, request).then((res) => res.data),

  updateMyProfile: (request: UpdateMyProfileRequest) =>
    apiClient.put<Member>('/api/members/me', request).then((res) => res.data),

  deleteMember: (id: string) => apiClient.delete(`/api/members/${id}`).then((res) => res.data),

  approveMember: (id: string) => apiClient.post(`/api/members/${id}/approve`).then((res) => res.data),

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
