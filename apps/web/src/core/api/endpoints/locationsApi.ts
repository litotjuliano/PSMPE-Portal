import { apiClient } from '../apiClient'
import type { PagedResult } from './adminApi'

/** One flat row per city - what the address picker indexes. */
export interface LocationRow {
  region: string
  province: string
  city: string
  zipCode: string | null
}

export interface RegionItem {
  id: string
  name: string
  sortOrder: number
  provinceCount: number
}

export interface ProvinceItem {
  id: string
  name: string
  regionId: string
  regionName: string
  cityCount: number
}

export interface CityItem {
  id: string
  name: string
  zipCode: string | null
  provinceId: string
  provinceName: string
  regionId: string
  regionName: string
}

export interface SaveRegionRequest {
  name: string
  sortOrder: number | null
}

export interface SaveProvinceRequest {
  name: string
  regionId: string
}

export interface SaveCityRequest {
  name: string
  zipCode: string | null
  provinceId: string
}

export interface ProvinceListParams {
  page?: number
  pageSize?: number
  search?: string
  regionId?: string
}

export interface CityListParams extends ProvinceListParams {
  provinceId?: string
}

const ADMIN = '/api/admin/locations'

export const locationsApi = {
  /** Read side for the address picker - any signed-in user. */
  getAll: () => apiClient.get<LocationRow[]>('/api/locations').then((res) => res.data),
}

export const locationsAdminApi = {
  getRegions: (search?: string) =>
    apiClient.get<RegionItem[]>(`${ADMIN}/regions`, { params: search ? { search } : {} }).then((res) => res.data),
  createRegion: (request: SaveRegionRequest) =>
    apiClient.post<RegionItem>(`${ADMIN}/regions`, request).then((res) => res.data),
  updateRegion: (id: string, request: SaveRegionRequest) =>
    apiClient.put<RegionItem>(`${ADMIN}/regions/${id}`, request).then((res) => res.data),
  deleteRegion: (id: string) => apiClient.delete(`${ADMIN}/regions/${id}`).then((res) => res.data),

  getProvinces: (params: ProvinceListParams = {}) =>
    apiClient.get<PagedResult<ProvinceItem>>(`${ADMIN}/provinces`, { params }).then((res) => res.data),
  createProvince: (request: SaveProvinceRequest) =>
    apiClient.post<ProvinceItem>(`${ADMIN}/provinces`, request).then((res) => res.data),
  updateProvince: (id: string, request: SaveProvinceRequest) =>
    apiClient.put<ProvinceItem>(`${ADMIN}/provinces/${id}`, request).then((res) => res.data),
  deleteProvince: (id: string) => apiClient.delete(`${ADMIN}/provinces/${id}`).then((res) => res.data),

  getCities: (params: CityListParams = {}) =>
    apiClient.get<PagedResult<CityItem>>(`${ADMIN}/cities`, { params }).then((res) => res.data),
  createCity: (request: SaveCityRequest) =>
    apiClient.post<CityItem>(`${ADMIN}/cities`, request).then((res) => res.data),
  updateCity: (id: string, request: SaveCityRequest) =>
    apiClient.put<CityItem>(`${ADMIN}/cities/${id}`, request).then((res) => res.data),
  deleteCity: (id: string) => apiClient.delete(`${ADMIN}/cities/${id}`).then((res) => res.data),
}
