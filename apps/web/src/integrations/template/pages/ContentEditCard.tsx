import type { FormEvent } from 'react'

interface ContentEditCardProps {
  isNew: boolean
  title: string
  body: string
  onTitleChange: (value: string) => void
  onBodyChange: (value: string) => void
  onSubmit: (event: FormEvent) => void
}

export const ContentEditCard = ({ isNew, title, body, onTitleChange, onBodyChange, onSubmit }: ContentEditCardProps) => {
  return (
    <div className="card max-w-2xl">
      <div className="card-header">
        <h6 className="card-title">{isNew ? 'New content' : 'Edit content'}</h6>
      </div>
      <form onSubmit={onSubmit} className="card-body space-y-4">
        <div>
          <label htmlFor="title" className="block font-medium text-default-900 text-sm mb-2">
            Title
          </label>
          <input
            id="title"
            type="text"
            className="form-input"
            required
            value={title}
            onChange={(e) => onTitleChange(e.target.value)}
          />
        </div>

        <div>
          <label htmlFor="body" className="block font-medium text-default-900 text-sm mb-2">
            Body
          </label>
          <textarea
            id="body"
            className="form-input"
            required
            rows={8}
            value={body}
            onChange={(e) => onBodyChange(e.target.value)}
          />
        </div>

        <button type="submit" className="btn bg-primary text-white">
          Save
        </button>
      </form>
    </div>
  )
}
