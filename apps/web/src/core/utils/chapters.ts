import { useEffect, useState } from 'react'
import { chaptersApi, type Chapter } from '../api/endpoints/chaptersApi'

let chaptersPromise: Promise<Chapter[]> | null = null
const listeners = new Set<() => void>()

/**
 * Loads the chapter list, which a Super Admin maintains in the database (GET /api/chapters).
 *
 * The promise is cached so every dropdown on a page shares one fetch. A failed load clears the
 * cache so the next call retries, and `invalidateChapters` clears it after an admin edit and tells
 * any mounted `useChapters` consumers to refetch.
 */
export function loadChapters(): Promise<Chapter[]> {
  if (chaptersPromise) return chaptersPromise
  const promise = chaptersApi.getAll().catch((err) => {
    if (chaptersPromise === promise) chaptersPromise = null
    throw err
  })
  chaptersPromise = promise
  return promise
}

export function invalidateChapters() {
  chaptersPromise = null
  listeners.forEach((listener) => listener())
}

/**
 * Chapters for a dropdown. `selectable` is the active ones, in the admin-set order, plus - when
 * `currentId` is given - that chapter even if inactive, so a member/event already on a retired
 * chapter still shows it instead of a blank select. `all` includes inactive ones (for id -> name lookups).
 */
export function useChapters(currentId?: string | null) {
  const [all, setAll] = useState<Chapter[]>([])
  const [loading, setLoading] = useState(true)
  const [reloadKey, setReloadKey] = useState(0)

  useEffect(() => {
    const listener = () => setReloadKey((k) => k + 1)
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    loadChapters()
      .then((chapters) => {
        if (!cancelled) setAll(chapters)
      })
      .catch(() => {
        if (!cancelled) setAll([])
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [reloadKey])

  const selectable = all.filter((c) => c.isActive || c.id === currentId)
  return { all, selectable, loading }
}
