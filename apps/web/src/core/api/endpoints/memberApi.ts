import { apiClient } from '../apiClient'
import type { PagedResult } from './adminApi'
import type { Member, MembershipStatusValue } from '../../types/member'

export interface GetMembersParams {
  page?: number
  pageSize?: number
  sortBy?: 'lastName' | 'membershipNo' | 'chapter' | 'status'
  sortDir?: 'asc' | 'desc'
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
  renewalDueDate: string | null
  nationalDuesReferenceNo: string | null
}

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
}
