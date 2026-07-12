import { useEffect, useState } from 'react'
import { TbX } from 'react-icons/tb'

interface FetchedFile {
  url: string
  contentType: string
}

interface FilePreviewModalProps {
  isOpen: boolean
  title: string
  /** Lazily invoked on open, not on mount - avoids fetching a file nobody asked to view yet. */
  fetchFile: () => Promise<FetchedFile | null>
  onClose: () => void
}

/**
 * Custom-built (no Preline dependency) centered overlay for viewing an uploaded image or PDF in
 * place of opening a new browser tab. PDFs render via a plain <iframe> (the browser's own built-in
 * viewer - no PDF.js/lightbox dependency needed for a single-page ≤2MB ID scan).
 */
export const FilePreviewModal = ({ isOpen, title, fetchFile, onClose }: FilePreviewModalProps) => {
  const [file, setFile] = useState<FetchedFile | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!isOpen) return
    let cancelled = false
    setLoading(true)
    setError(null)
    fetchFile()
      .then((result) => {
        if (cancelled) return
        if (result) {
          setFile(result)
        } else {
          setError('No file has been uploaded yet.')
        }
      })
      .catch(() => {
        if (!cancelled) setError('Unable to load this file. Please try again.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
    // Only re-fetch when the modal transitions open, not whenever the caller passes a new
    // `fetchFile` reference (most callers pass an inline arrow function) - refetching on every
    // parent re-render while the modal stays open would be wasteful and could race.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen])

  useEffect(() => {
    if (!isOpen) return
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [isOpen, onClose])

  const handleClose = () => {
    if (file) URL.revokeObjectURL(file.url)
    setFile(null)
    onClose()
  }

  if (!isOpen) return null

  return (
    <div className="fixed inset-0 z-100 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={handleClose} />
      <div className="relative card w-full max-w-3xl max-h-[90vh] flex flex-col">
        <div className="card-header flex items-center justify-between">
          <h6 className="card-title">{title}</h6>
          <button type="button" onClick={handleClose} className="btn size-9 rounded-full btn-sm hover:bg-default-150">
            <TbX className="text-xl" />
          </button>
        </div>
        <div className="card-body flex-1 overflow-auto flex items-center justify-center min-h-64">
          {loading && <p className="text-sm text-default-500">Loading…</p>}
          {!loading && error && <p className="text-sm text-danger">{error}</p>}
          {!loading && !error && file && file.contentType === 'application/pdf' && (
            <iframe src={file.url} title={title} className="w-full h-[70vh]" />
          )}
          {!loading && !error && file && file.contentType !== 'application/pdf' && (
            <img src={file.url} alt={title} className="max-w-full max-h-[70vh] object-contain" />
          )}
        </div>
        <div className="card-footer flex items-center justify-end">
          <button type="button" onClick={handleClose} className="btn border border-default-200">
            Close
          </button>
        </div>
      </div>
    </div>
  )
}
