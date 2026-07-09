import { apiClient } from '../apiClient'
import type { ContentItem, CreateContentItemRequest, UpdateContentItemRequest } from '../../types/content'

export const contentApi = {
  getAll: () => apiClient.get<ContentItem[]>('/api/content').then((res) => res.data),

  getById: (id: string) => apiClient.get<ContentItem>(`/api/content/${id}`).then((res) => res.data),

  create: (request: CreateContentItemRequest) =>
    apiClient.post<ContentItem>('/api/content', request).then((res) => res.data),

  update: (id: string, request: UpdateContentItemRequest) =>
    apiClient.put(`/api/content/${id}`, request).then((res) => res.data),

  remove: (id: string) => apiClient.delete(`/api/content/${id}`).then((res) => res.data),
}
