export interface Layout {
  id: string
  name: string
  definition: string
  isSystemLayout: boolean
  ownerId: string | null
}

export interface CreateLayoutRequest {
  name: string
  definition: string
}
