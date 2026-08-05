import { useEffect, useRef, useState, type FormEvent } from 'react'
import { accountApi } from '../api/endpoints/accountApi'
import { uploadApi } from '../api/endpoints/uploadApi'
import { useAuth } from '../auth/useAuth'
import { describeError } from '../utils/apiError'

/**
 * What every account can change about itself: display name, photo, and password.
 *
 * Shown alone at /profile for administrative accounts, which have no Member row and so have no
 * membership application, and alongside the membership profile for members. Grew out of the
 * photo-only card that first replaced the wizard for admins - photo alone is not a profile, and
 * before this no account of any role could change its own name or password at all.
 */
export function AccountSection() {
  const { user, updateCachedDisplayName } = useAuth()

  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [savingName, setSavingName] = useState(false)
  const [nameError, setNameError] = useState<string | null>(null)
  const [nameSaved, setNameSaved] = useState(false)

  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [changingPassword, setChangingPassword] = useState(false)
  const [passwordError, setPasswordError] = useState<string | null>(null)
  const [passwordChanged, setPasswordChanged] = useState(false)

  const [photoUrl, setPhotoUrl] = useState<string | null>(null)
  const [photoError, setPhotoError] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)
  // Held in a ref so cleanup revokes the URL actually on screen, not a stale capture.
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
          if (blob) URL.revokeObjectURL(blob.url)
          return
        }
        showPhoto(blob?.url ?? null)
      })
      .catch(() => {
        // fetchMyPhotoUrl maps 404 to null, so reaching here is a real fault - but it must not
        // block the upload control.
        if (!cancelled) setPhotoError('Could not load your current photo.')
      })
    return () => {
      cancelled = true
      if (objectUrlRef.current) {
        URL.revokeObjectURL(objectUrlRef.current)
        objectUrlRef.current = null
      }
    }
  }, [])

  async function handleNameSubmit(event: FormEvent) {
    event.preventDefault()
    setSavingName(true)
    setNameError(null)
    setNameSaved(false)
    try {
      const updated = await accountApi.updateAccount({ displayName })
      // Without this the topbar keeps the old name until the token expires, which reads as a
      // failed save even though the server accepted it.
      updateCachedDisplayName(updated.displayName)
      setNameSaved(true)
    } catch (err) {
      setNameError(describeError(err, 'Could not save your name. Please try again.'))
    } finally {
      setSavingName(false)
    }
  }

  async function handlePasswordSubmit(event: FormEvent) {
    event.preventDefault()
    setChangingPassword(true)
    setPasswordError(null)
    setPasswordChanged(false)
    try {
      await accountApi.changePassword({ currentPassword, newPassword })
      setPasswordChanged(true)
      setCurrentPassword('')
      setNewPassword('')
    } catch (err) {
      setPasswordError(describeError(err, 'Could not change your password. Please try again.'))
    } finally {
      setChangingPassword(false)
    }
  }

  async function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) return

    setUploading(true)
    setPhotoError(null)
    try {
      await uploadApi.uploadMyPhoto(file)
      const blob = await uploadApi.fetchMyPhotoUrl()
      showPhoto(blob?.url ?? null)
    } catch (err) {
      setPhotoError(describeError(err, 'Could not upload your photo. Please try again.'))
    } finally {
      setUploading(false)
      // Cleared so re-selecting the same file still fires onChange.
      if (fileInputRef.current) fileInputRef.current.value = ''
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="card">
        <div className="card-body flex flex-col gap-4">
          <h6 className="text-base font-semibold">Account</h6>

          <div className="flex items-center gap-4">
            {photoUrl ? (
              <img src={photoUrl} alt="Account photo" className="size-24 rounded-full object-cover" />
            ) : (
              <div className="flex size-24 items-center justify-center rounded-full bg-default-100 text-xs text-default-500">
                No photo
              </div>
            )}
            <div className="flex flex-col gap-1">
              <input
                ref={fileInputRef}
                type="file"
                accept="image/*"
                onChange={handleFileChange}
                disabled={uploading}
                className="text-sm"
              />
              {uploading && <p className="text-sm text-default-500">Uploading…</p>}
              {photoError && <p className="text-sm font-medium text-danger">{photoError}</p>}
            </div>
          </div>

          <form onSubmit={handleNameSubmit} className="flex flex-col gap-2">
            <label htmlFor="account-display-name" className="text-sm font-medium">
              Display name
            </label>
            <input
              id="account-display-name"
              type="text"
              value={displayName}
              onChange={(event) => setDisplayName(event.target.value)}
              className="form-input"
            />
            <p className="text-xs text-default-500">
              Signed in as {user?.email} — contact an administrator to change your email address.
            </p>
            {nameError && <p className="text-sm font-medium text-danger">{nameError}</p>}
            {nameSaved && <p className="text-sm font-medium text-success">Name updated.</p>}
            <div>
              <button type="submit" disabled={savingName} className="btn bg-primary text-white">
                {savingName ? 'Saving…' : 'Save name'}
              </button>
            </div>
          </form>
        </div>
      </div>

      <div className="card">
        <div className="card-body">
          <h6 className="mb-4 text-base font-semibold">Change password</h6>
          <form onSubmit={handlePasswordSubmit} className="flex max-w-md flex-col gap-3">
            <div className="flex flex-col gap-1">
              <label htmlFor="account-current-password" className="text-sm font-medium">
                Current password
              </label>
              <input
                id="account-current-password"
                type="password"
                autoComplete="current-password"
                value={currentPassword}
                onChange={(event) => setCurrentPassword(event.target.value)}
                className="form-input"
              />
            </div>
            <div className="flex flex-col gap-1">
              <label htmlFor="account-new-password" className="text-sm font-medium">
                New password
              </label>
              <input
                id="account-new-password"
                type="password"
                autoComplete="new-password"
                value={newPassword}
                onChange={(event) => setNewPassword(event.target.value)}
                className="form-input"
              />
            </div>
            {passwordError && <p className="text-sm font-medium text-danger">{passwordError}</p>}
            {passwordChanged && (
              <p className="text-sm font-medium text-success">
                Password changed. Other devices already signed in stay signed in until their session expires.
              </p>
            )}
            <div>
              <button type="submit" disabled={changingPassword} className="btn bg-primary text-white">
                {changingPassword ? 'Changing…' : 'Change password'}
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  )
}
