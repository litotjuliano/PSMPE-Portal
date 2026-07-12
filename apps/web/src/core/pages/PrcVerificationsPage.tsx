import { useEffect, useState } from 'react'
import { memberApi } from '../api/endpoints/memberApi'
import type { Member } from '../types/member'
import { PrcVerificationsTable, PageBreadcrumb, PageMeta } from '../../integrations/template'

const PAGE_SIZE = 20

export function PrcVerificationsPage() {
  const [members, setMembers] = useState<Member[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)

  const refetch = () =>
    memberApi
      .getMembers({ page, pageSize: PAGE_SIZE, sortBy: 'membershipNo', pendingPrcVerificationOnly: true })
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
    memberApi.approvePrcVerification(id).then(refetch)
  }

  const handleReject = (id: string, reason: string) => {
    memberApi.rejectPrcVerification(id, reason).then(refetch)
  }

  return (
    <>
      <PageMeta title="PRC Verifications" />
      <main>
        <PageBreadcrumb title="PRC Verifications" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <PrcVerificationsTable
            members={members}
            onApprove={handleApprove}
            onReject={handleReject}
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
