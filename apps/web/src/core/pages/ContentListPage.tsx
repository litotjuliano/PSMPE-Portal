import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { contentApi } from '../api/endpoints/contentApi'
import type { ContentItem } from '../types/content'
import { Button } from '../components/ui/Button'

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

  if (loading) {
    return <p className="text-sm text-gray-500">Loading…</p>
  }

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-gray-900">Content</h1>
        <Link to="/content/new">
          <Button>New content</Button>
        </Link>
      </div>

      <ul className="divide-y divide-gray-200 rounded-md border border-gray-200 bg-white">
        {items.map((item) => (
          <li key={item.id} className="flex items-center justify-between px-4 py-3">
            <div>
              <p className="font-medium text-gray-900">{item.title}</p>
              <p className="text-xs text-gray-500">Status: {item.status}</p>
            </div>
            <div className="flex gap-2">
              <Link to={`/content/${item.id}`} className="text-sm text-indigo-600 hover:underline">
                Edit
              </Link>
              <button
                onClick={() => handleDelete(item.id)}
                className="text-sm text-red-600 hover:underline"
              >
                Delete
              </button>
            </div>
          </li>
        ))}
        {items.length === 0 && <li className="px-4 py-6 text-sm text-gray-500">No content yet.</li>}
      </ul>
    </div>
  )
}
