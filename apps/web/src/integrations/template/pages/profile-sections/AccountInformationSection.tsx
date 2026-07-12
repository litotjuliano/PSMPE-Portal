import type { Member } from '../../../../core/types/member'

interface AccountInformationSectionProps {
  member: Member
  accountDisplayName: string
}

/** No Edit action here - Email is out of scope for editing and there's nothing else
 *  self-service on this tab, same as the wizard's read-only Account Information step. */
export const AccountInformationSection = ({ member, accountDisplayName }: AccountInformationSectionProps) => (
  <div className="flex flex-col gap-4">
    <h6 className="font-semibold text-default-800">Account Information</h6>
    <p className="text-sm text-default-500">Your account was already created at sign-up - nothing to change here.</p>
    <div className="text-sm">
      <span className="text-default-500">Email</span> <span className="font-semibold text-default-800">{member.email}</span>
    </div>
    <div className="text-sm">
      <span className="text-default-500">Display Name</span> <span className="font-semibold text-default-800">{accountDisplayName}</span>
    </div>
  </div>
)
