import { Link } from 'react-router-dom'
import { LuChevronDown, LuChevronUp, LuPlus, LuSquarePen, LuTrash2 } from 'react-icons/lu'
import type { GetMembersParams } from '../../../core/api/endpoints/memberApi'
import type { Member } from '../../../core/types/member'
import { MembershipStatus } from '../../../core/types/member'

type SortableColumn = NonNullable<GetMembersParams['sortBy']>

interface MembersTableProps {
  members: Member[]
  onDelete: (id: string) => void
  sortBy: SortableColumn
  sortDir: 'asc' | 'desc'
  onSortChange: (column: SortableColumn) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

const statusLabels: Record<number, string> = {
  [MembershipStatus.Pending]: 'Pending',
  [MembershipStatus.Active]: 'Active',
  [MembershipStatus.Expired]: 'Expired',
  [MembershipStatus.Deactivated]: 'Deactivated',
}

const statusClasses: Record<number, string> = {
  [MembershipStatus.Pending]: 'bg-warning/10 text-warning',
  [MembershipStatus.Active]: 'bg-success/10 text-success',
  [MembershipStatus.Expired]: 'bg-danger/10 text-danger',
  [MembershipStatus.Deactivated]: 'bg-default-150 text-default-600',
}

function initialsOf(firstName: string, lastName: string) {
  return `${firstName.charAt(0)}${lastName.charAt(0)}`.toUpperCase()
}

function SortableHeader({
  column,
  label,
  sortBy,
  sortDir,
  onSortChange,
}: {
  column: SortableColumn
  label: string
  sortBy: SortableColumn
  sortDir: 'asc' | 'desc'
  onSortChange: (column: SortableColumn) => void
}) {
  const isActive = sortBy === column
  return (
    <th className="px-3.5 py-3 text-start">
      <button type="button" onClick={() => onSortChange(column)} className="inline-flex items-center gap-1 hover:text-default-900">
        {label}
        {isActive && (sortDir === 'asc' ? <LuChevronUp className="size-3.5" /> : <LuChevronDown className="size-3.5" />)}
      </button>
    </th>
  )
}

export const MembersTable = ({
  members,
  onDelete,
  sortBy,
  sortDir,
  onSortChange,
  page,
  pageSize,
  totalCount,
  onPageChange,
}: MembersTableProps) => {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  return (
    <div className="card">
      <div className="card-header flex justify-between items-center">
        <h6 className="card-title">Members</h6>
        <Link to="/members/new" className="btn btn-sm bg-primary text-white">
          <LuPlus className="size-4 me-1" />
          New member
        </Link>
      </div>

      <div className="flex flex-col">
        <div className="overflow-x-auto">
          <div className="min-w-full inline-block align-middle">
            <div className="overflow-hidden">
              <table className="min-w-full divide-y divide-default-200">
                <thead className="bg-default-150">
                  <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
                    <SortableHeader column="lastName" label="Name" sortBy={sortBy} sortDir={sortDir} onSortChange={onSortChange} />
                    <SortableHeader
                      column="membershipNo"
                      label="Membership No."
                      sortBy={sortBy}
                      sortDir={sortDir}
                      onSortChange={onSortChange}
                    />
                    <SortableHeader column="chapter" label="Chapter" sortBy={sortBy} sortDir={sortDir} onSortChange={onSortChange} />
                    <SortableHeader column="status" label="Status" sortBy={sortBy} sortDir={sortDir} onSortChange={onSortChange} />
                    <th className="px-3.5 py-3 text-start">Email</th>
                    <th className="px-3.5 py-3 text-start">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {members.map((member) => (
                    <tr key={member.id} className="text-default-800 font-normal text-sm whitespace-nowrap">
                      <td className="flex py-3 px-3.5 items-center gap-3">
                        <div className="w-9 h-9 flex items-center justify-center rounded-full bg-primary/10 text-primary font-semibold text-xs">
                          {initialsOf(member.firstName, member.lastName)}
                        </div>
                        <span className="font-semibold">
                          {member.firstName} {member.lastName}
                        </span>
                      </td>
                      <td className="py-3 px-3.5">{member.membershipNo}</td>
                      <td className="py-3 px-3.5">{member.chapter}</td>
                      <td className="py-3 px-3.5">
                        <span className={`inline-flex items-center py-0.5 px-2.5 rounded text-xs font-medium ${statusClasses[member.status]}`}>
                          {statusLabels[member.status]}
                        </span>
                      </td>
                      <td className="py-3 px-3.5 text-default-500">{member.email}</td>
                      <td className="py-3 px-3.5">
                        <div className="flex items-center gap-1.5">
                          <Link
                            to={`/members/${member.id}`}
                            className="btn btn-icon size-8 hover:bg-default-150 rounded-full text-default-500"
                            aria-label="Edit"
                          >
                            <LuSquarePen className="size-4" />
                          </Link>
                          <button
                            onClick={() => onDelete(member.id)}
                            className="btn btn-icon size-8 hover:bg-danger/10 hover:text-danger rounded-full text-default-500"
                            aria-label="Delete"
                          >
                            <LuTrash2 className="size-4" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                  {members.length === 0 && (
                    <tr>
                      <td colSpan={6} className="py-6 px-3.5 text-center text-default-500">
                        No members yet.
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
