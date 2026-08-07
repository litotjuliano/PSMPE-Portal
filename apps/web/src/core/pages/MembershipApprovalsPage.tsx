import { useEffect, useState } from 'react'
import { memberApi } from '../api/endpoints/memberApi'
import type { Member } from '../types/member'
import { describeError } from '../utils/apiError'
import { ApproveMembershipModal, MembershipApprovalsTable, PageBreadcrumb, PageMeta } from '../../integrations/template'

const PAGE_SIZE = 20

export function MembershipApprovalsPage() {
  const [members, setMembers] = useState<Member[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)

  const refetch = () =>
    memberApi
      .getMembers({ page, pageSize: PAGE_SIZE, sortBy: 'membershipNo', pendingApprovalOnly: true })
      .then((result) => {
        setMembers(result.items)
        setTotalCount(result.totalCount)
      })

  useEffect(() => {
    setLoading(true)
    refetch().finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page])

  // Approval assigns PSMPE's control number, so it can't be a one-click action any more - the
  // dialog collects it and stays open on a duplicate.
  const [approving, setApproving] = useState<Member | null>(null)

  const handleConfirmApprove = async (membershipNo: string) => {
    if (!approving) return
    try {
      await memberApi.approveMember(approving.id, membershipNo)
    } catch (err) {
      // Rethrown so the modal shows it and stays open; previously this call had no catch at all
      // and a rejected approval failed silently.
      throw new Error(describeError(err, 'Could not approve this application. Please try again.'))
    }
    setApproving(null)
    await refetch()
  }

  return (
    <>
      <PageMeta title="Membership Approvals" />
      <main>
        <PageBreadcrumb title="Membership Approvals" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <MembershipApprovalsTable
            members={members}
            onApprove={(member) => setApproving(member)}
            page={page}
            pageSize={PAGE_SIZE}
            totalCount={totalCount}
            onPageChange={setPage}
          />
        )}
        <ApproveMembershipModal
          isOpen={approving !== null}
          memberName={approving ? `${approving.firstName} ${approving.lastName}` : undefined}
          onConfirm={handleConfirmApprove}
          onCancel={() => setApproving(null)}
        />
      </main>
    </>
  )
}
