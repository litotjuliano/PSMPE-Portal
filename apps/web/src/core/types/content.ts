export const ContentStatus = {
  Draft: 0,
  Published: 1,
  Archived: 2,
} as const

export type ContentStatusValue = (typeof ContentStatus)[keyof typeof ContentStatus]

export interface ContentItem {
  id: string
  title: string
  body: string
  status: ContentStatusValue
  ownerId: string
  layoutId: string | null
  createdAt: string
  updatedAt: string | null
}

export interface CreateContentItemRequest {
  title: string
  body: string
  layoutId: string | null
}

export interface UpdateContentItemRequest {
  title: string
  body: string
  status: ContentStatusValue
  layoutId: string | null
}
