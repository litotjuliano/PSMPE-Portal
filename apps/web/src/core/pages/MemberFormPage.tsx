import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { memberApi } from '../api/endpoints/memberApi'
import { adminApi, type UserSummary } from '../api/endpoints/adminApi'
import { MembershipStatus } from '../types/member'
import { MemberFormCard, type MemberFormState, PageBreadcrumb, PageMeta } from '../../integrations/template'

const emptyState: MemberFormState = {
  userId: '',
  membershipNo: '',
  firstName: '',
  middleName: '',
  lastName: '',
  suffix: '',
  birthdate: '',
  gender: '',
  address: '',
  prcLicenseNo: '',
  chapter: '',
  company: '',
  status: MembershipStatus.Pending,
  renewalDueDate: '',
  nationalDuesReferenceNo: '',
}

export function MemberFormPage() {
  const { id } = useParams()
  const isNew = !id || id === 'new'
  const navigate = useNavigate()

  const [state, setState] = useState<MemberFormState>(emptyState)
  const [users, setUsers] = useState<UserSummary[]>([])
  const [loading, setLoading] = useState(!isNew)

  useEffect(() => {
    if (isNew) {
      adminApi.getUsers({ pageSize: 200 }).then((result) => setUsers(result.items))
      return
    }
    if (id) {
      memberApi.getMemberById(id).then((member) => {
        setState({
          userId: member.userId,
          membershipNo: member.membershipNo,
          firstName: member.firstName,
          middleName: member.middleName ?? '',
          lastName: member.lastName,
          suffix: member.suffix ?? '',
          birthdate: member.birthdate ?? '',
          gender: member.gender ?? '',
          address: member.address ?? '',
          prcLicenseNo: member.prcLicenseNo ?? '',
          chapter: member.chapter,
          company: member.company ?? '',
          status: member.status,
          renewalDueDate: member.renewalDueDate ?? '',
          nationalDuesReferenceNo: member.nationalDuesReferenceNo ?? '',
        })
        setLoading(false)
      })
    }
  }, [id, isNew])

  const handleChange = <K extends keyof MemberFormState>(field: K, value: MemberFormState[K]) => {
    setState((current) => ({ ...current, [field]: value }))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (isNew) {
      await memberApi.createMember({
        userId: state.userId,
        membershipNo: state.membershipNo,
        firstName: state.firstName,
        middleName: state.middleName || null,
        lastName: state.lastName,
        suffix: state.suffix || null,
        birthdate: state.birthdate || null,
        gender: state.gender || null,
        address: state.address || null,
        prcLicenseNo: state.prcLicenseNo || null,
        chapter: state.chapter,
        company: state.company || null,
        renewalDueDate: state.renewalDueDate || null,
        nationalDuesReferenceNo: state.nationalDuesReferenceNo || null,
      })
    } else if (id) {
      await memberApi.updateMember(id, {
        firstName: state.firstName,
        middleName: state.middleName || null,
        lastName: state.lastName,
        suffix: state.suffix || null,
        birthdate: state.birthdate || null,
        gender: state.gender || null,
        address: state.address || null,
        prcLicenseNo: state.prcLicenseNo || null,
        chapter: state.chapter,
        company: state.company || null,
        status: state.status,
        renewalDueDate: state.renewalDueDate || null,
        nationalDuesReferenceNo: state.nationalDuesReferenceNo || null,
      })
    }
    navigate('/members')
  }

  return (
    <>
      <PageMeta title={isNew ? 'New member' : 'Edit member'} />
      <main>
        <PageBreadcrumb title={isNew ? 'New member' : 'Edit member'} subtitle="Members" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <MemberFormCard isNew={isNew} state={state} onChange={handleChange} onSubmit={handleSubmit} users={users} />
        )}
      </main>
    </>
  )
}
