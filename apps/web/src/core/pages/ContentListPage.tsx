import { useEffect, useState } from 'react'
import { contentApi } from '../api/endpoints/contentApi'
import type { ContentItem } from '../types/content'
import { ContentListCard, PageBreadcrumb, PageMeta } from '../../integrations/template'

export function ContentListPage() {
  const [items, setItems] = useState<ContentItem[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    contentApi
      .getAll()
      .then(setItems)
      .finally(() => setLoading(false))
  }, [])

  async function handleDelete(id: string) {
    await contentApi.remove(id)
    setItems((current) => current.filter((item) => item.id !== id))
  }

  return (
    <>
      <PageMeta title="Content" />
      <main>
        <PageBreadcrumb title="Content" />
        {loading ? <p className="text-sm text-default-500">Loading…</p> : <ContentListCard items={items} onDelete={handleDelete} />}
      </main>
    </>
  )
}
