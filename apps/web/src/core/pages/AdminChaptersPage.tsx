import { useCallback, useEffect, useState } from 'react'
import { chaptersAdminApi, type Chapter } from '../api/endpoints/chaptersApi'
import { describeError } from '../utils/apiError'
import { invalidateChapters } from '../utils/chapters'
import {
  ChapterFormModal,
  ChaptersTable,
  ConfirmationModal,
  PageBreadcrumb,
  PageMeta,
  Toast,
  type ChapterFormInitial,
  type ChapterFormValues,
  type ChapterStatusFilter,
  type ToastStatus,
} from '../../integrations/template'

/** Super Admin maintenance of the chapters offered on member and event forms. */
export function AdminChaptersPage() {
  const [rows, setRows] = useState<Chapter[]>([])
  const [loading, setLoading] = useState(true)
  const [reloadKey, setReloadKey] = useState(0)

  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<ChapterStatusFilter>('')

  const [form, setForm] = useState<{ initial: ChapterFormInitial | null } | null>(null)
  const [deleting, setDeleting] = useState<Chapter | null>(null)
  const [toast, setToast] = useState<ToastStatus | null>(null)

  // Debounced like AdminLocationsPage: one fetch per pause in typing, not per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => setSearch(searchInput), 350)
    return () => clearTimeout(timer)
  }, [searchInput])

  const reload = useCallback(() => {
    invalidateChapters()
    setReloadKey((k) => k + 1)
  }, [])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    chaptersAdminApi
      .getAll({
        ...(search ? { search } : {}),
        ...(statusFilter ? { isActive: statusFilter === 'active' } : {}),
      })
      .then((items) => {
        if (!cancelled) setRows(items)
      })
      .catch((err) => {
        if (!cancelled) setToast({ ok: false, text: describeError(err, 'Could not load chapters.') })
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [search, statusFilter, reloadKey])

  const handleSave = async (values: ChapterFormValues) => {
    const id = form?.initial?.id
    try {
      await (id ? chaptersAdminApi.update(id, values) : chaptersAdminApi.create(values))
    } catch (err) {
      // The modal shows this next to the form so a duplicate-name conflict can be fixed in place.
      throw new Error(describeError(err, 'Could not save.'))
    }
    setForm(null)
    setToast({ ok: true, text: `Saved ${values.name}.` })
    reload()
  }

  const handleDelete = async () => {
    if (!deleting) return
    const target = deleting
    try {
      await chaptersAdminApi.remove(target.id)
      setToast({ ok: true, text: `Deleted ${target.name}.` })
      reload()
    } catch (err) {
      // A chapter still in use comes back as a 409 whose message suggests deactivating instead.
      setToast({ ok: false, text: describeError(err, 'Could not delete.') })
    } finally {
      setDeleting(null)
    }
  }

  return (
    <>
      <PageMeta title="Chapters" />
      <main>
        <PageBreadcrumb title="Chapters" />
        <p className="text-sm text-default-600 mb-4">
          The chapters offered on member and event forms. Renaming a chapter updates every member and event on it.
          A chapter that members or events still use can't be deleted - deactivate it instead.
        </p>

        <ChaptersTable
          rows={rows}
          loading={loading}
          searchInput={searchInput}
          onSearchInputChange={setSearchInput}
          statusFilter={statusFilter}
          onStatusFilterChange={setStatusFilter}
          onAdd={() => setForm({ initial: null })}
          onEdit={(row) =>
            setForm({ initial: { id: row.id, name: row.name, sortOrder: row.sortOrder, isActive: row.isActive } })
          }
          onDelete={setDeleting}
        />
      </main>

      {form && <ChapterFormModal initial={form.initial} onSave={handleSave} onCancel={() => setForm(null)} />}

      <ConfirmationModal
        isOpen={deleting !== null}
        title={`Delete ${deleting?.name ?? ''}?`}
        message={
          deleting && (deleting.memberCount > 0 || deleting.eventCount > 0)
            ? `${deleting.name} is used by ${deleting.memberCount} member(s) and ${deleting.eventCount} event(s), so it can't be deleted. Deactivate it from Edit instead.`
            : 'This chapter will be removed. It is not used by any member or event.'
        }
        confirmLabel="Delete"
        onConfirm={handleDelete}
        onCancel={() => setDeleting(null)}
      />
      <Toast status={toast} onDismiss={() => setToast(null)} />
    </>
  )
}
