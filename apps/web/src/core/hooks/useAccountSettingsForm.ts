import { useState, type FormEvent } from 'react'
import { accountApi } from '../api/endpoints/accountApi'
import { useAuth } from '../auth/useAuth'
import { describeError } from '../utils/apiError'

/**
 * Display name and password self-service - the same two forms and two API calls used by the
 * standalone Account card (administrative accounts with no Member row, see AccountSection) and the
 * Account & Security profile tab (members). Extracted so both call sites share one fetch/save path
 * instead of duplicating the handlers wholesale.
 */
export function useAccountSettingsForm() {
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

  return {
    user,
    displayName,
    setDisplayName,
    savingName,
    nameError,
    nameSaved,
    handleNameSubmit,
    currentPassword,
    setCurrentPassword,
    newPassword,
    setNewPassword,
    changingPassword,
    passwordError,
    passwordChanged,
    handlePasswordSubmit,
  }
}
