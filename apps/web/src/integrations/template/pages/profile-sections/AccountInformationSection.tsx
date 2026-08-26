import { useAccountSettingsForm } from '../../../../core/hooks/useAccountSettingsForm'

/**
 * Account & Security - Email plus the self-service Display Name and Change Password forms.
 * Display Name used to be read-only here while the actually-editable version lived in a separate
 * card below the tabs (AccountSection); this tab is now the single home for both, and that
 * standalone card is no longer rendered for members (still used as-is for administrative accounts,
 * which have no Member row and so no tabs to hold this content).
 */
export const AccountInformationSection = () => {
  const {
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
  } = useAccountSettingsForm()

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-4">
        <h6 className="font-semibold text-default-800">Account &amp; Security</h6>

        <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-3 gap-4 text-sm">
          <div>
            <span className="block font-medium text-default-900 text-sm mb-2">Email</span>
            <span className="font-semibold text-default-800">{user?.email ?? '-'}</span>
          </div>
        </div>
      </div>

      <div className="border-t border-default-200 pt-4">
        <span className="text-xs font-semibold uppercase tracking-wide text-teal">Display Name</span>
        <form onSubmit={handleNameSubmit} className="flex flex-col gap-2 mt-3 max-w-md">
          <label htmlFor="account-display-name" className="block font-medium text-default-900 text-sm">
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

      <div className="border-t border-default-200 pt-4">
        <span className="text-xs font-semibold uppercase tracking-wide text-teal">Change Password</span>
        <form onSubmit={handlePasswordSubmit} className="flex flex-col gap-3 mt-3 max-w-md">
          <div className="flex flex-col gap-1">
            <label htmlFor="account-current-password" className="block font-medium text-default-900 text-sm">
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
            <label htmlFor="account-new-password" className="block font-medium text-default-900 text-sm">
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
  )
}
