import { useState } from 'react'
import { Link } from 'react-router-dom'
import { LuCheck, LuEye, LuX } from 'react-icons/lu'
import type { Member } from '../../../core/types/member'
import { uploadApi } from '../../../core/api/endpoints/uploadApi'
import { StandardButton } from '../components/shared/StandardButton'
import { ConfirmationModal } from '../components/shared/ConfirmationModal'
import { FilePreviewModal } from '../components/shared/FilePreviewModal'

interface PrcVerificationsTableProps {
  members: Member[]
  onApprove: (id: string) => void
  onReject: (id: string, reason: string) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

function initialsOf(firstName: string, lastName: string) {
  return `${firstName.charAt(0)}${lastName.charAt(0)}`.toUpperCase()
}

export const PrcVerificationsTable = ({
  members,
  onApprove,
  onReject,
  page,
  pageSize,
  totalCount,
  onPageChange,
}: PrcVerificationsTableProps) => {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const [rejectingId, setRejectingId] = useState<string | null>(null)
  const [previewingId, setPreviewingId] = useState<string | null>(null)

  const handleReject = (reason?: string) => {
    if (rejectingId && reason) {
      onReject(rejectingId, reason)
    }
    setRejectingId(null)
  }

  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">PRC License Verifications</h6>
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
                    <th className="px-3.5 py-3 text-start">Current PRC No.</th>
                    <th className="px-3.5 py-3 text-start">Pending PRC No.</th>
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
                      <td className="py-3 px-3.5">{member.prcLicenseNo || '-'}</td>
                      <td className="py-3 px-3.5">
                        {member.pendingPrcLicenseNo ?? <span className="text-default-500">Never reviewed</span>}
                      </td>
                      <td className="py-3 px-3.5">
                        <div className="flex items-center gap-1.5">
                          <StandardButton variant="success" size="sm" icon={LuCheck} onClick={() => onApprove(member.id)}>
                            Approve
                          </StandardButton>
                          <StandardButton variant="danger" size="sm" icon={LuX} onClick={() => setRejectingId(member.id)}>
                            Reject
                          </StandardButton>
                          <StandardButton variant="view" size="sm" icon={LuEye} onClick={() => setPreviewingId(member.id)}>
                            View ID
                          </StandardButton>
                        </div>
                      </td>
                    </tr>
                  ))}
                  {members.length === 0 && (
                    <tr>
                      <td colSpan={5} className="py-6 px-3.5 text-center text-default-500">
                        No pending PRC verifications.
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
        isOpen={rejectingId !== null}
        title="Reject PRC verification"
        message="This will discard the pending PRC change and notify the member with your reason."
        confirmLabel="Reject"
        confirmVariant="danger"
        reasonRequired
        onConfirm={(reason) => handleReject(reason)}
        onCancel={() => setRejectingId(null)}
      />

      {previewingId && (
        <FilePreviewModal
          isOpen
          title="PRC ID Document"
          fetchFile={() => uploadApi.fetchMemberPrcIdUrl(previewingId)}
          onClose={() => setPreviewingId(null)}
        />
      )}
    </div>
  )
}
