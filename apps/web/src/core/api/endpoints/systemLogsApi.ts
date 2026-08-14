import { apiClient } from '../apiClient'
import type { PagedResult } from './adminApi'

export interface AuditLogEntry {
  id: string
  eventType: string
  actorUserId: string | null
  actorEmail: string | null
  actorIp: string | null
  targetType: string | null
  targetId: string | null
  metadata: string | null
  createdAt: string
}

export interface GetAuditLogParams {
  page?: number
  pageSize?: number
  search?: string
  eventType?: string
  from?: string
  to?: string
}

export const ErrorSource = { Backend: 0, Frontend: 1 } as const
export type ErrorSourceValue = (typeof ErrorSource)[keyof typeof ErrorSource]

export interface ErrorLogEntry {
  id: string
  source: ErrorSourceValue
  exceptionType: string | null
  message: string
  stackTrace: string | null
  requestPath: string | null
  requestMethod: string | null
  url: string | null
  userId: string | null
  userEmail: string | null
  userAgent: string | null
  metadata: string | null
  createdAt: string
}

export interface GetErrorLogParams {
  page?: number
  pageSize?: number
  search?: string
  source?: ErrorSourceValue
  from?: string
  to?: string
}

export const systemLogsApi = {
  getAuditLog: (params: GetAuditLogParams = {}) =>
    apiClient.get<PagedResult<AuditLogEntry>>('/api/admin/audit-log', { params }).then((res) => res.data),
  getErrorLog: (params: GetErrorLogParams = {}) =>
    apiClient.get<PagedResult<ErrorLogEntry>>('/api/admin/error-log', { params }).then((res) => res.data),
}
