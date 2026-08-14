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

export const systemLogsApi = {
  getAuditLog: (params: GetAuditLogParams = {}) =>
    apiClient.get<PagedResult<AuditLogEntry>>('/api/admin/audit-log', { params }).then((res) => res.data),
}
