import { apiClient } from '../apiClient'

export interface Chapter {
  id: string
  name: string
  sortOrder: number
  /** Inactive chapters aren't offered for new selections, but members/events already on one keep it. */
  isActive: boolean
  memberCount: number
  eventCount: number
}

/** Null sortOrder on create means "last"; omitted isActive on update leaves it unchanged. */
export interface SaveChapterRequest {
  name: string
  sortOrder?: number | null
  isActive?: boolean
}

export interface ChapterListParams {
  search?: string
  isActive?: boolean
}

const ADMIN = '/api/admin/chapters'

export const chaptersApi = {
  /** Read side for dropdowns and filters - any signed-in user. Includes inactive chapters. */
  getAll: () => apiClient.get<Chapter[]>('/api/chapters').then((res) => res.data),
}

export const chaptersAdminApi = {
  getAll: (params: ChapterListParams = {}) =>
    apiClient.get<Chapter[]>(ADMIN, { params }).then((res) => res.data),
  create: (request: SaveChapterRequest) => apiClient.post<Chapter>(ADMIN, request).then((res) => res.data),
  update: (id: string, request: SaveChapterRequest) =>
    apiClient.put<Chapter>(`${ADMIN}/${id}`, request).then((res) => res.data),
  remove: (id: string) => apiClient.delete(`${ADMIN}/${id}`).then((res) => res.data),
}
