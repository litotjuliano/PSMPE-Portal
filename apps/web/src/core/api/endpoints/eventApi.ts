import { apiClient } from '../apiClient'
import type { PagedResult } from './adminApi'

export const EventMode = {
  Onsite: 'Onsite',
  Online: 'Online',
} as const
export type EventModeValue = (typeof EventMode)[keyof typeof EventMode]

export const EventRegistrationStatus = {
  Registered: 'Registered',
  PaymentSubmitted: 'PaymentSubmitted',
  PaymentVerified: 'PaymentVerified',
  Attended: 'Attended',
  EvaluationSubmitted: 'EvaluationSubmitted',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
} as const
export type EventRegistrationStatusValue = (typeof EventRegistrationStatus)[keyof typeof EventRegistrationStatus]

export interface EventSession {
  id: string
  title: string
  startsAt: string
  endsAt: string
  order: number
}

export interface EventSessionInput {
  id: string | null
  title: string
  startsAt: string
  endsAt: string
  order: number
}

export interface Event {
  id: string
  title: string
  description: string | null
  chapter: string | null
  venue: string | null
  startsAt: string
  endsAt: string
  capacity: number | null
  registeredCount: number
  fee: number
  /** Null means "TBD" - see Event.CpdUnitsOnsite's backend doc comment. */
  cpdUnitsOnsite: number | null
  cpdUnitsOnline: number | null
  sessions: EventSession[]
}

export interface CreateEventRequest {
  title: string
  description: string | null
  chapter: string | null
  venue: string | null
  startsAt: string
  endsAt: string
  capacity: number | null
  fee: number
}

export interface UpdateEventRequest extends CreateEventRequest {
  cpdUnitsOnsite: number | null
  cpdUnitsOnline: number | null
  sessions: EventSessionInput[]
}

export interface EventRegistration {
  id: string
  eventId: string
  eventTitle: string
  eventStartsAt: string
  memberId: string
  memberName: string
  membershipNo: string | null
  mode: EventModeValue
  status: EventRegistrationStatusValue
  sessionsAttended: number
  totalSessions: number
  evaluationRating: number | null
  evaluationComments: string | null
  evaluationSubmittedAt: string | null
  creditUnits: number | null
}

export interface EventRosterEntry {
  registrationId: string
  memberId: string
  memberName: string
  membershipNo: string | null
  mode: EventModeValue
  status: EventRegistrationStatusValue
  attendedSessionIds: string[]
  totalSessions: number
  paymentId: string | null
  paymentStatus: string | null
  paymentIsCash: boolean | null
  paymentRejectedReason: string | null
  evaluationRating: number | null
  evaluationSubmittedAt: string | null
  creditUnits: number | null
}

export interface EventRoster {
  eventId: string
  eventTitle: string
  sessions: EventSession[]
  registrants: EventRosterEntry[]
}

export interface MyCpdRegistration {
  registrationId: string
  eventId: string
  eventTitle: string
  eventStartsAt: string
  mode: EventModeValue
  status: EventRegistrationStatusValue
  sessionsAttended: number
  totalSessions: number
  creditUnits: number | null
}

export interface MyCpdSummary {
  totalCreditUnits: number
  registrations: MyCpdRegistration[]
}

export const eventApi = {
  getEvents: (params: { page?: number; pageSize?: number; search?: string; chapter?: string; upcomingOnly?: boolean } = {}) =>
    apiClient.get<PagedResult<Event>>('/api/events', { params }).then((res) => res.data),

  getEvent: (id: string) => apiClient.get<Event>(`/api/events/${id}`).then((res) => res.data),

  createEvent: (request: CreateEventRequest) => apiClient.post<Event>('/api/events', request).then((res) => res.data),

  updateEvent: (id: string, request: UpdateEventRequest) =>
    apiClient.put<Event>(`/api/events/${id}`, request).then((res) => res.data),

  register: (eventId: string, mode: EventModeValue) =>
    apiClient.post<EventRegistration>(`/api/events/${eventId}/register`, { mode }).then((res) => res.data),

  cancelRegistration: (registrationId: string) =>
    apiClient.post(`/api/events/registrations/${registrationId}/cancel`).then((res) => res.data),

  submitPayment: (registrationId: string, request: { amount: number; referenceNo: string | null; paidOn: string }) =>
    apiClient.post(`/api/events/registrations/${registrationId}/payment`, request).then((res) => res.data),

  /** Attaches proof to the payment just created by submitPayment - reuses the existing generic
   *  payment-proof endpoint, since a Payment's proof isn't tied to which Kind it is. */
  uploadPaymentProof: (paymentId: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return apiClient.post(`/api/payments/${paymentId}/proof`, form).then((res) => res.data)
  },

  recordCashPayment: (registrationId: string, amount: number) =>
    apiClient.post(`/api/events/registrations/${registrationId}/payment/cash`, { amount }).then((res) => res.data),

  recordAttendance: (eventId: string, registrants: { registrationId: string; sessionIds: string[] }[]) =>
    apiClient.post(`/api/events/${eventId}/roster/attendance`, { registrants }).then((res) => res.data),

  submitEvaluation: (registrationId: string, rating: number, comments: string | null) =>
    apiClient.post(`/api/events/registrations/${registrationId}/evaluation`, { rating, comments }).then((res) => res.data),

  getRoster: (eventId: string) => apiClient.get<EventRoster>(`/api/events/${eventId}/roster`).then((res) => res.data),

  getMyCpd: () => apiClient.get<MyCpdSummary>('/api/members/me/cpd').then((res) => res.data),

  /** Fetched as a blob, same reasoning as paymentApi.fetchProofUrl - an authenticated download
   *  can't be a plain <a href>. */
  downloadCertificate: async (registrationId: string): Promise<{ url: string } | null> => {
    try {
      const response = await apiClient.get(`/api/events/registrations/${registrationId}/certificate`, { responseType: 'blob' })
      return { url: URL.createObjectURL(response.data) }
    } catch {
      return null
    }
  },
}
