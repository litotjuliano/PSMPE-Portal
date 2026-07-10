import { useEffect, useState } from 'react'
import { memberApi, type GetMembersParams } from '../api/endpoints/memberApi'
import type { Member } from '../types/member'
import { MembersTable, PageBreadcrumb, PageMeta } from '../../integrations/template'

const PAGE_SIZE = 20

export function MembersPage() {
  const [members, setMembers] = useState<Member[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [sortBy, setSortBy] = useState<NonNullable<GetMembersParams['sortBy']>>('lastName')
  const [sortDir, setSortDir] = useState<NonNullable<GetMembersParams['sortDir']>>('asc')
  const [loading, setLoading] = useState(true)

  const refetch = () =>
    memberApi.getMembers({ page, pageSize: PAGE_SIZE, sortBy, sortDir }).then((result) => {
      setMembers(result.items)
      setTotalCount(result.totalCount)
    })

  useEffect(() => {
    setLoading(true)
    refetch().finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, sortBy, sortDir])

  const handleDelete = (id: string) => {
    memberApi.deleteMember(id).then(refetch)
  }

  const handleSortChange = (column: NonNullable<GetMembersParams['sortBy']>) => {
    if (column === sortBy) {
      setSortDir((current) => (current === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortBy(column)
      setSortDir('asc')
    }
    setPage(1)
  }

  return (
    <>
      <PageMeta title="Members" />
      <main>
        <PageBreadcrumb title="Members" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <MembersTable
            members={members}
            onDelete={handleDelete}
            sortBy={sortBy}
            sortDir={sortDir}
            onSortChange={handleSortChange}
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
