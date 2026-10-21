import { useEffect, useState, type FormEvent } from 'react'
import { StandardButton } from '../components/shared/StandardButton'

export interface ChapterFormValues {
  name: string
  sortOrder: number | null
  isActive: boolean
}

export interface ChapterFormInitial {
  id: string
  name: string
  sortOrder: number
  isActive: boolean
}

interface ChapterFormModalProps {
  /** Null means create. */
  initial: ChapterFormInitial | null
  onSave: (values: ChapterFormValues) => Promise<void>
  onCancel: () => void
}

export const ChapterFormModal = ({ initial, onSave, onCancel }: ChapterFormModalProps) => {
  const [name, setName] = useState(initial?.name ?? '')
  const [sortOrder, setSortOrder] = useState(initial ? String(initial.sortOrder) : '')
  const [isActive, setIsActive] = useState(initial?.isActive ?? true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onCancel])

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (!name.trim()) return setError('Name is required.')

    setSaving(true)
    setError(null)
    try {
      await onSave({
        name: name.trim(),
        sortOrder: sortOrder.trim() === '' ? null : Number(sortOrder),
        isActive,
      })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save.')
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-100 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={onCancel} />
      <form className="relative card w-full max-w-md" onSubmit={(e) => void handleSubmit(e)}>
        <div className="card-header">
          <h6 className="card-title">{initial ? 'Edit' : 'Add'} chapter</h6>
        </div>
        <div className="card-body flex flex-col gap-4 text-sm">
          <div>
            <label htmlFor="chapter-name" className="block font-medium text-default-900 text-sm mb-2">
              Name
            </label>
            <input
              id="chapter-name"
              className="form-input"
              value={name}
              maxLength={64}
              autoFocus
              onChange={(e) => setName(e.target.value)}
            />
          </div>
          <div>
            <label htmlFor="chapter-sort" className="block font-medium text-default-900 text-sm mb-2">
              List order (optional)
            </label>
            <input
              id="chapter-sort"
              type="number"
              className="form-input"
              value={sortOrder}
              placeholder="Leave blank to put it last"
              onChange={(e) => setSortOrder(e.target.value)}
            />
          </div>
          <label className="flex items-start gap-2">
            <input
              type="checkbox"
              className="form-checkbox mt-0.5"
              checked={isActive}
              onChange={(e) => setIsActive(e.target.checked)}
            />
            <span>
              <span className="font-medium text-default-900">Active</span>
              <span className="block text-default-500">
                Inactive chapters can't be chosen for new members or events. Anyone already on one keeps it.
              </span>
            </span>
          </label>
          {error && (
            <p role="alert" className="text-sm text-danger">
              {error}
            </p>
          )}
        </div>
        <div className="card-footer flex items-center justify-end gap-2">
          <StandardButton variant="secondary" onClick={onCancel} disabled={saving}>
            Cancel
          </StandardButton>
          <StandardButton variant="primary" type="submit" loading={saving} loadingLabel="Saving…">
            Save
          </StandardButton>
        </div>
      </form>
    </div>
  )
}
