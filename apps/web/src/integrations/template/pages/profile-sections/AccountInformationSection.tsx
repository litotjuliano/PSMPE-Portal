import { useAuth } from '../../../../core/auth/useAuth'

/**
 * Read-only - Email and Display Name have no self-service edit surface here (Email isn't
 * editable anywhere in the product; Display Name is only ever set at registration). Sourced from
 * the auth session rather than the Member record, since neither field lives on Member.
 */
export const AccountInformationSection = () => {
  const { user } = useAuth()

  return (
    <div className="flex flex-col gap-4">
      <h6 className="font-semibold text-default-800">Account Information</h6>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-sm">
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Email</span>
          <span className="font-semibold text-default-800">{user?.email ?? '-'}</span>
        </div>
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">Display Name</span>
          <span className="font-semibold text-default-800">{user?.displayName ?? '-'}</span>
        </div>
      </div>
    </div>
  )
}
