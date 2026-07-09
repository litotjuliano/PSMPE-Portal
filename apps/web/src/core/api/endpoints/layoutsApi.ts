import { apiClient } from '../apiClient'
import type { CreateLayoutRequest, Layout } from '../../types/layout'

export const layoutsApi = {
  getAll: () => apiClient.get<Layout[]>('/api/layouts').then((res) => res.data),

  create: (request: CreateLayoutRequest) =>
    apiClient.post<Layout>('/api/layouts', request).then((res) => res.data),

  remove: (id: string) => apiClient.delete(`/api/layouts/${id}`).then((res) => res.data),
}
