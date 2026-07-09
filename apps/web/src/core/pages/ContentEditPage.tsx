import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { contentApi } from '../api/endpoints/contentApi'
import { ContentStatus } from '../types/content'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'

export function ContentEditPage() {
  const { id } = useParams()
  const isNew = !id || id === 'new'
  const navigate = useNavigate()

  const [title, setTitle] = useState('')
  const [body, setBody] = useState('')
  const [loading, setLoading] = useState(!isNew)

  useEffect(() => {
    if (!isNew && id) {
      contentApi.getById(id).then((item) => {
        setTitle(item.title)
        setBody(item.body)
        setLoading(false)
      })
    }
  }, [id, isNew])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (isNew) {
      await contentApi.create({ title, body, layoutId: null })
    } else if (id) {
      await contentApi.update(id, { title, body, status: ContentStatus.Draft, layoutId: null })
    }
    navigate('/content')
  }

  if (loading) {
    return <p className="text-sm text-gray-500">Loading…</p>
  }

  return (
    <form onSubmit={handleSubmit} className="max-w-xl space-y-4">
      <h1 className="text-2xl font-semibold text-gray-900">{isNew ? 'New content' : 'Edit content'}</h1>
      <Input id="title" label="Title" required value={title} onChange={(e) => setTitle(e.target.value)} />
      <div>
        <label htmlFor="body" className="block text-sm font-medium text-gray-900">
          Body
        </label>
        <textarea
          id="body"
          required
          rows={8}
          value={body}
          onChange={(e) => setBody(e.target.value)}
          className="mt-1 block w-full rounded-md border-0 px-3 py-1.5 text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 focus:ring-2 focus:ring-inset focus:ring-indigo-600"
        />
      </div>
      <Button type="submit">Save</Button>
    </form>
  )
}
