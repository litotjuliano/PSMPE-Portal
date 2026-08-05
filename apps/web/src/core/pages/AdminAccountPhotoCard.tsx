import { useEffect, useRef, useState } from 'react'
import { uploadApi } from '../api/endpoints/uploadApi'
import { describeError } from '../utils/apiError'

/**
 * What an administrative account sees at /profile instead of the membership application wizard.
 *
 * Admin/Manager/Accounts/Super Admin accounts deliberately have no Member row (see
 * MembersController.UpdateMyProfile), so the wizard's save always returned 403 for them - the
 * user just saw a silent failure. Uploads are keyed by UserId rather than MemberId, though, so
 * the account photo genuinely works for these accounts and is the one thing worth offering here.
 */
export function AdminAccountPhotoCard() {
  const [photoUrl, setPhotoUrl] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)
  // Kept in a ref so the cleanup below revokes the URL actually on screen, not a stale capture.
  const objectUrlRef = useRef<string | null>(null)

  function showPhoto(next: string | null) {
    if (objectUrlRef.current) {
      URL.revokeObjectURL(objectUrlRef.current)
    }
    objectUrlRef.current = next
    setPhotoUrl(next)
  }

  useEffect(() => {
    let cancelled = false
    uploadApi
      .fetchMyPhotoUrl()
      .then((blob) => {
        if (cancelled) {
          // Revoke immediately rather than leaking: nothing will render this one.
          if (blob) URL.revokeObjectURL(blob.url)
          return
        }
        showPhoto(blob?.url ?? null)
      })
      .catch(() => {
        // A missing photo is the normal state and fetchMyPhotoUrl already maps 404 to null, so
        // anything reaching here is a real fault - but it must not block the upload control.
        if (!cancelled) setError('Could not load your current photo.')
      })
    return () => {
      cancelled = true
      if (objectUrlRef.current) {
        URL.revokeObjectURL(objectUrlRef.current)
        objectUrlRef.current = null
      }
    }
  }, [])

  async function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) return

    setUploading(true)
    setError(null)
    try {
      await uploadApi.uploadMyPhoto(file)
      const blob = await uploadApi.fetchMyPhotoUrl()
      showPhoto(blob?.url ?? null)
    } catch (err) {
      setError(describeError(err, 'Could not upload your photo. Please try again.'))
    } finally {
      setUploading(false)
      // Clear the input so re-selecting the same file still fires onChange.
      if (fileInputRef.current) fileInputRef.current.value = ''
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="card border border-info/30 bg-info/10">
        <div className="card-body text-sm font-medium text-info">
          Administrative accounts don&apos;t have a membership application. You can still set the
          account photo shown across the portal.
        </div>
      </div>

      <div className="card">
        <div className="card-body flex flex-col items-start gap-4">
          <h6 className="text-base font-semibold">Account photo</h6>

          {photoUrl ? (
            <img src={photoUrl} alt="Account photo" className="size-32 rounded-full object-cover" />
          ) : (
            <div className="flex size-32 items-center justify-center rounded-full bg-default-100 text-sm text-default-500">
              No photo
            </div>
          )}

          {error && <p className="text-sm font-medium text-danger">{error}</p>}

          <input
            ref={fileInputRef}
            type="file"
            accept="image/*"
            onChange={handleFileChange}
            disabled={uploading}
            className="text-sm"
          />
          {uploading && <p className="text-sm text-default-500">Uploading…</p>}
        </div>
      </div>
    </div>
  )
}
