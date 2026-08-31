import { apiClient } from '../apiClient'
import type { PagedResult } from './adminApi'

export const PaymentKind = {
  NewMembership: 'NewMembership',
  Renewal: 'Renewal',
  EventRegistration: 'EventRegistration',
} as const

export type PaymentKindValue = (typeof PaymentKind)[keyof typeof PaymentKind]

export const PaymentStatus = {
  Submitted: 'Submitted',
  Verified: 'Verified',
  Rejected: 'Rejected',
} as const

export type PaymentStatusValue = (typeof PaymentStatus)[keyof typeof PaymentStatus]

export interface Payment {
  id: string
  memberId: string
  memberName: string
  membershipNo: string | null
  kind: PaymentKindValue
  amount: number
  referenceNo: string | null
  paidOn: string
  /** The storage key itself is never sent - it embeds the member's name and birthdate. */
  hasProof: boolean
  status: PaymentStatusValue
  rejectedReason: string | null
  decidedAt: string | null
  /** The renewal date this payment bought, recorded at verification. */
  coversUntil: string | null
  createdAt: string
  /** Set only when kind is EventRegistration - which event, since "Event registration" alone
   *  doesn't say which one. Null for NewMembership/Renewal. */
  eventTitle: string | null
}

export interface SubmitPaymentRequest {
  amount: number
  referenceNo: string | null
  paidOn: string
}

export interface MembershipFees {
  membershipFee: number
  shippingFee: number
  annualDues: number
  registrationTotal: number
}

export const paymentApi = {
  /** Admin queue. Defaults server-side to Submitted, the only status needing action. */
  getPayments: (params: { page?: number; pageSize?: number; status?: PaymentStatusValue } = {}) =>
    apiClient.get<PagedResult<Payment>>('/api/payments', { params }).then((res) => res.data),

  getMyPayments: () => apiClient.get<Payment[]>('/api/payments/me').then((res) => res.data),

  /** Stores a proof for another member and returns its key, for the approval request to carry.
   *  Admin-only; used when a walk-in has no payment on record yet. */
  uploadProofForMember: (memberId: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return apiClient.post<{ storageKey: string }>(`/api/payments/member/${memberId}/proof`, form).then((res) => res.data)
  },

  getPaymentsForMember: (memberId: string) =>
    apiClient.get<Payment[]>(`/api/payments/member/${memberId}`).then((res) => res.data),

  submitMyPayment: (request: SubmitPaymentRequest) =>
    apiClient.post<Payment>('/api/payments/me', request).then((res) => res.data),

  uploadProof: (paymentId: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return apiClient.post(`/api/payments/${paymentId}/proof`, form).then((res) => res.data)
  },

  /** Fetched as a blob so the authenticated request carries the bearer token - a plain <img src>
   *  can't. Same approach as the member document previews. */
  fetchProofUrl: async (paymentId: string): Promise<{ url: string; contentType: string } | null> => {
    try {
      const response = await apiClient.get(`/api/payments/${paymentId}/proof`, { responseType: 'blob' })
      return { url: URL.createObjectURL(response.data), contentType: response.data.type }
    } catch {
      return null
    }
  },

  verifyPayment: (id: string) => apiClient.post(`/api/payments/${id}/verify`).then((res) => res.data),

  rejectPayment: (id: string, reason: string) =>
    apiClient.post(`/api/payments/${id}/reject`, { reason }).then((res) => res.data),

  getFees: () => apiClient.get<MembershipFees>('/api/payments/fees').then((res) => res.data),

  updateFees: (fees: { membershipFee: number; shippingFee: number; annualDues: number }) =>
    apiClient.put('/api/payments/fees', fees).then((res) => res.data),
}
