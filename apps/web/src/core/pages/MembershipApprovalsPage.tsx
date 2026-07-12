import { useEffect, useState } from 'react'
import { memberApi } from '../api/endpoints/memberApi'
import type { Member } from '../types/member'
import { MembershipApprovalsTable, PageBreadcrumb, PageMeta } from '../../integrations/template'

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

  const handleApprove = (id: string) => {
    memberApi.approveMember(id).then(refetch)
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
            onApprove={handleApprove}
            page={page}
            pageSize={PAGE_SIZE}
            totalCount={totalCount}
            onPageChange={setPage}
          />
        )}
      </main>
    </>
  )
}
