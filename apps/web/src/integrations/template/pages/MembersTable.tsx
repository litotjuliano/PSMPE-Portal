import { useState } from 'react'
import { Link } from 'react-router-dom'
import { LuCheck, LuChevronDown, LuChevronUp, LuEye, LuPlus, LuSquarePen, LuTrash2, LuX } from 'react-icons/lu'
import type { GetMembersParams } from '../../../core/api/endpoints/memberApi'
import { uploadApi } from '../../../core/api/endpoints/uploadApi'
import type { Member } from '../../../core/types/member'
import { MembershipStatus, type MembershipStatusValue } from '../../../core/types/member'
import { ConfirmationModal } from '../components/shared/ConfirmationModal'
import { FilePreviewModal } from '../components/shared/FilePreviewModal'
import { StandardButton } from '../components/shared/StandardButton'

type SortableColumn = NonNullable<GetMembersParams['sortBy']>

/**
 * Which of the three member lists this is. All three are the same query against
 * `GET /api/members` with different filters, so they share one table rather than the three
 * near-identical ones this replaced - only the trailing columns and the row actions differ.
 */
export type MembersView = 'all' | 'pendingApproval' | 'pendingRmp'

interface MembersTableProps {
  members: Member[]
  view: MembersView
  /** Gates New/Edit/Delete on the 'all' view - false for an Approval user, who only works the
   *  pendingApproval/pendingRmp queues below and otherwise sees members read-only. */
  canManageMembers: boolean
  /** 'all' view only. The raw, un-debounced input value - MembersPage owns the debounce timer
   *  that turns this into the actual filter sent to the server. */
  searchInput?: string
  onSearchInputChange?: (value: string) => void
  statusFilter?: MembershipStatusValue | null
  onStatusFilterChange?: (status: MembershipStatusValue | null) => void
  sortBy: SortableColumn
  sortDir: 'asc' | 'desc'
  onSortChange: (column: SortableColumn) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
  /** 'all' only - the queues are for deciding, not for destroying records. */
  onDelete?: (id: string) => void
  /** 'pendingApproval' only. Passes the whole member so the dialog can name who it's numbering. */
  onApprove?: (member: Member) => void
  /** 'pendingRmp' only. Verify takes no input; reject requires a reason for the audit trail. */
  onVerifyRmp?: (id: string) => void
  onRejectRmp?: (id: string, reason: string) => void
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

const VIEW_COPY: Record<MembersView, { title: string; empty: string }> = {
  all: { title: 'Members', empty: 'No members yet.' },
  pendingApproval: { title: 'Pending Membership Approvals', empty: 'No pending applications.' },
  pendingRmp: { title: 'RMP License Verifications', empty: 'No pending RMP verifications.' },
}

/** Name, Membership No. and Chapter are shared; the rest is per-view, plus Actions. */
const COLUMN_COUNT: Record<MembersView, number> = {
  all: 6,
  pendingApproval: 5,
  pendingRmp: 6,
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
  view,
  canManageMembers,
  searchInput,
  onSearchInputChange,
  statusFilter,
  onStatusFilterChange,
  sortBy,
  sortDir,
  onSortChange,
  page,
  pageSize,
  totalCount,
  onPageChange,
  onDelete,
  onApprove,
  onVerifyRmp,
  onRejectRmp,
}: MembersTableProps) => {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const [deletingMember, setDeletingMember] = useState<Member | null>(null)
  const [rejectingId, setRejectingId] = useState<string | null>(null)
  const [previewingId, setPreviewingId] = useState<string | null>(null)

  const handleReject = (reason?: string) => {
    // ConfirmationModal's reasonRequired already blocks an empty submit; this guards the type.
    if (rejectingId && reason) onRejectRmp?.(rejectingId, reason)
    setRejectingId(null)
  }

  return (
    <div className="card">
      <div className="card-header flex justify-between items-center">
        <h6 className="card-title">{VIEW_COPY[view].title}</h6>
        {view === 'all' && canManageMembers && (
          <StandardButton to="/members/new" size="sm" variant="on-primary" icon={LuPlus}>
            New member
          </StandardButton>
        )}
      </div>

      {view === 'all' && (
        <div className="card-header flex flex-wrap items-center gap-3 border-t border-default-200">
          <input
            type="text"
            className="form-input max-w-xs"
            placeholder="Search by name, membership no., or email…"
            value={searchInput ?? ''}
            onChange={(e) => onSearchInputChange?.(e.target.value)}
          />
          <select
            className="form-input max-w-40"
            value={statusFilter ?? ''}
            onChange={(e) =>
              onStatusFilterChange?.(e.target.value === '' ? null : (Number(e.target.value) as MembershipStatusValue))
            }
          >
            <option value="">All statuses</option>
            <option value={MembershipStatus.Pending}>Pending</option>
            <option value={MembershipStatus.Active}>Active</option>
            <option value={MembershipStatus.Expired}>Expired</option>
            <option value={MembershipStatus.Deactivated}>Deactivated</option>
          </select>
        </div>
      )}

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

                    {view === 'all' && (
                      <>
                        <SortableHeader column="status" label="Status" sortBy={sortBy} sortDir={sortDir} onSortChange={onSortChange} />
                        <th className="px-3.5 py-3 text-start">Email</th>
                      </>
                    )}
                    {view === 'pendingApproval' && (
                      <SortableHeader
                        column="submittedAt"
                        label="Applied"
                        sortBy={sortBy}
                        sortDir={sortDir}
                        onSortChange={onSortChange}
                      />
                    )}
                    {view === 'pendingRmp' && (
                      <>
                        <th className="px-3.5 py-3 text-start">Current RMP No.</th>
                        <th className="px-3.5 py-3 text-start">Pending RMP No.</th>
                      </>
                    )}

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
                        <Link to={`/members/${member.id}`} className="font-semibold hover:text-primary">
                          {member.firstName} {member.lastName}
                        </Link>
                      </td>
                      <td className="py-3 px-3.5">
                        {member.membershipNo ?? <span className="text-default-500">Not yet assigned</span>}
                      </td>
                      <td className="py-3 px-3.5">{member.chapter}</td>

                      {view === 'all' && (
                        <>
                          <td className="py-3 px-3.5">
                            <span
                              className={`inline-flex items-center py-0.5 px-2.5 rounded text-xs font-medium ${statusClasses[member.status]}`}
                            >
                              {statusLabels[member.status]}
                            </span>
                          </td>
                          <td className="py-3 px-3.5 text-default-500">{member.email}</td>
                        </>
                      )}
                      {view === 'pendingApproval' && (
                        // submittedAt, not createdAt: an application is "applied" when the member
                        // submits it, not when the draft row first appeared mid-wizard.
                        <td className="py-3 px-3.5">
                          {member.submittedAt ? new Date(member.submittedAt).toLocaleDateString() : '-'}
                        </td>
                      )}
                      {view === 'pendingRmp' && (
                        <>
                          <td className="py-3 px-3.5">{member.prcLicenseNo || '-'}</td>
                          <td className="py-3 px-3.5">
                            {member.pendingPrcLicenseNo ?? <span className="text-default-500">Never reviewed</span>}
                          </td>
                        </>
                      )}

                      <td className="py-3 px-3.5">
                        <div className="flex items-center gap-1.5">
                          {view === 'all' && (
                            canManageMembers ? (
                              <>
                                <Link
                                  to={`/members/${member.id}`}
                                  className="btn btn-icon size-8 hover:bg-default-150 rounded-full text-default-500"
                                  aria-label="Edit"
                                >
                                  <LuSquarePen className="size-4" />
                                </Link>
                                <button
                                  onClick={() => setDeletingMember(member)}
                                  className="btn btn-icon size-8 hover:bg-danger/10 hover:text-danger rounded-full text-default-500"
                                  aria-label="Delete"
                                >
                                  <LuTrash2 className="size-4" />
                                </button>
                              </>
                            ) : (
                              <Link to={`/members/${member.id}`} className="btn btn-sm border border-default-200">
                                View
                              </Link>
                            )
                          )}
                          {view === 'pendingApproval' && (
                            <>
                              <StandardButton variant="success" size="sm" icon={LuCheck} onClick={() => onApprove?.(member)}>
                                Approve
                              </StandardButton>
                              <Link to={`/members/${member.id}`} className="btn btn-sm border border-default-200">
                                View
                              </Link>
                            </>
                          )}
                          {view === 'pendingRmp' && (
                            <>
                              <StandardButton variant="success" size="sm" icon={LuCheck} onClick={() => onVerifyRmp?.(member.id)}>
                                Approve
                              </StandardButton>
                              <StandardButton variant="danger" size="sm" icon={LuX} onClick={() => setRejectingId(member.id)}>
                                Reject
                              </StandardButton>
                              <StandardButton variant="view" size="sm" icon={LuEye} onClick={() => setPreviewingId(member.id)}>
                                View ID
                              </StandardButton>
                            </>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                  {members.length === 0 && (
                    <tr>
                      <td colSpan={COLUMN_COUNT[view]} className="py-6 px-3.5 text-center text-default-500">
                        {VIEW_COPY[view].empty}
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

      <ConfirmationModal
        isOpen={deletingMember !== null}
        title="Delete this member?"
        message={
          deletingMember
            ? `This permanently removes ${deletingMember.firstName} ${deletingMember.lastName}'s membership profile. Their login account is not affected.`
            : undefined
        }
        confirmLabel="Delete"
        confirmVariant="danger"
        onConfirm={() => {
          if (deletingMember) onDelete?.(deletingMember.id)
          setDeletingMember(null)
        }}
        onCancel={() => setDeletingMember(null)}
      />

      <ConfirmationModal
        isOpen={rejectingId !== null}
        title="Reject RMP verification"
        message="This will discard the pending RMP change and notify the member with your reason."
        confirmLabel="Reject"
        confirmVariant="danger"
        reasonRequired
        onConfirm={(reason) => handleReject(reason)}
        onCancel={() => setRejectingId(null)}
      />

      {previewingId && (
        <FilePreviewModal
          isOpen
          title="RMP ID Document"
          fetchFile={() => uploadApi.fetchMemberPrcIdUrl(previewingId)}
          onClose={() => setPreviewingId(null)}
        />
      )}
    </div>
  )
}
