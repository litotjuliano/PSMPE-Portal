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
  address: string | null
  prcLicenseNo: string | null
  chapter: string
  company: string | null
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
  address: string | null
  prcLicenseNo: string | null
  chapter: string
  company: string | null
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
  address: string | null
  prcLicenseNo: string | null
  chapter: string
  company: string | null
  memberType: string
  /** Asserts a new PRC ID was just uploaded in this edit - required whenever prcLicenseNo changes
   *  on an already-submitted application (see MemberService.UpsertMyProfileAsync). */
  prcIdReuploaded: boolean
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
}
