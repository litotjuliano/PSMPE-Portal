import { Link } from 'react-router-dom'
import type { Member } from '../../../core/types/member'

interface MembershipApprovalsTableProps {
  members: Member[]
  onApprove: (id: string) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

function initialsOf(firstName: string, lastName: string) {
  return `${firstName.charAt(0)}${lastName.charAt(0)}`.toUpperCase()
}

export const MembershipApprovalsTable = ({
  members,
  onApprove,
  page,
  pageSize,
  totalCount,
  onPageChange,
}: MembershipApprovalsTableProps) => {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">Pending Membership Approvals</h6>
      </div>

      <div className="flex flex-col">
        <div className="overflow-x-auto">
          <div className="min-w-full inline-block align-middle">
            <div className="overflow-hidden">
              <table className="min-w-full divide-y divide-default-200">
                <thead className="bg-default-150">
                  <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
                    <th className="px-3.5 py-3 text-start">Member No.</th>
                    <th className="px-3.5 py-3 text-start">Name</th>
                    <th className="px-3.5 py-3 text-start">Chapter</th>
                    <th className="px-3.5 py-3 text-start">Applied</th>
                    <th className="px-3.5 py-3 text-start">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {members.map((member) => (
                    <tr key={member.id} className="text-default-800 font-normal text-sm whitespace-nowrap">
                      <td className="py-3 px-3.5">{member.membershipNo}</td>
                      <td className="flex py-3 px-3.5 items-center gap-3">
                        <div className="w-9 h-9 flex items-center justify-center rounded-full bg-primary/10 text-primary font-semibold text-xs">
                          {initialsOf(member.firstName, member.lastName)}
                        </div>
                        <Link to={`/members/${member.id}`} className="font-semibold hover:text-primary">
                          {member.firstName} {member.lastName}
                        </Link>
                      </td>
                      <td className="py-3 px-3.5">{member.chapter}</td>
                      <td className="py-3 px-3.5">{new Date(member.createdAt).toLocaleDateString()}</td>
                      <td className="py-3 px-3.5">
                        <div className="flex items-center gap-1.5">
                          <button onClick={() => onApprove(member.id)} className="btn btn-sm bg-success text-white">
                            Approve
                          </button>
                          <Link to={`/members/${member.id}`} className="btn btn-sm border border-default-200">
                            View
                          </Link>
                        </div>
                      </td>
                    </tr>
                  ))}
                  {members.length === 0 && (
                    <tr>
                      <td colSpan={5} className="py-6 px-3.5 text-center text-default-500">
                        No pending applications.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>

      <div className="card-footer flex items-center justify-between">
        <span className="text-sm text-default-500">
          Page {page} of {totalPages} ({totalCount} total)
        </span>
        <div className="flex items-center gap-1.5">
          <button
            type="button"
            className="btn btn-sm border border-default-200 disabled:opacity-50"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
          >
            Previous
          </button>
          <button
            type="button"
            className="btn btn-sm border border-default-200 disabled:opacity-50"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
          >
            Next
          </button>
        </div>
      </div>
    </div>
  )
}
