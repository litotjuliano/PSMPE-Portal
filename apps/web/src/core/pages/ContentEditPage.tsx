import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { contentApi } from '../api/endpoints/contentApi'
import { ContentStatus } from '../types/content'
import { ContentEditCard, PageBreadcrumb, PageMeta } from '../../integrations/template'

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

  return (
    <>
      <PageMeta title={isNew ? 'New content' : 'Edit content'} />
      <main>
        <PageBreadcrumb title={isNew ? 'New content' : 'Edit content'} subtitle="Content" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <ContentEditCard isNew={isNew} title={title} body={body} onTitleChange={setTitle} onBodyChange={setBody} onSubmit={handleSubmit} />
        )}
      </main>
    </>
  )
}
