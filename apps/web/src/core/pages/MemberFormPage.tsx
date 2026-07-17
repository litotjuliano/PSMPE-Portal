import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { memberApi, type ProfileCompleteness } from '../api/endpoints/memberApi'
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
  civilStatus: '',
  address: '',
  mobileNumber: '',
  housePhone: '',
  website: '',
  facebookUrl: '',
  linkedInUrl: '',
  xUrl: '',
  instagramUrl: '',
  prcLicenseNo: '',
  ptrNumber: '',
  tin: '',
  chapter: '',
  employmentStatus: '',
  company: '',
  position: '',
  businessAddress: '',
  yearsOfPractice: '',
  specialization: '',
  skills: '',
  memberType: '',
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
  const [approvedAt, setApprovedAt] = useState<string | null>(null)
  const [isInGracePeriod, setIsInGracePeriod] = useState(false)
  const [completeness, setCompleteness] = useState<ProfileCompleteness | null>(null)

  const load = () => {
    if (id) {
      return memberApi.getMemberById(id).then((member) => {
        setState({
          userId: member.userId,
          membershipNo: member.membershipNo,
          firstName: member.firstName,
          middleName: member.middleName ?? '',
          lastName: member.lastName,
          suffix: member.suffix ?? '',
          birthdate: member.birthdate ?? '',
          gender: member.gender ?? '',
          civilStatus: member.civilStatus ?? '',
          address: member.address ?? '',
          mobileNumber: member.mobileNumber ?? '',
          housePhone: member.housePhone ?? '',
          website: member.website ?? '',
          facebookUrl: member.facebookUrl ?? '',
          linkedInUrl: member.linkedInUrl ?? '',
          xUrl: member.xUrl ?? '',
          instagramUrl: member.instagramUrl ?? '',
          prcLicenseNo: member.prcLicenseNo ?? '',
          ptrNumber: member.ptrNumber ?? '',
          tin: member.tin ?? '',
          chapter: member.chapter,
          employmentStatus: member.employmentStatus ?? '',
          company: member.company ?? '',
          position: member.position ?? '',
          businessAddress: member.businessAddress ?? '',
          yearsOfPractice: member.yearsOfPractice !== null && member.yearsOfPractice !== undefined ? String(member.yearsOfPractice) : '',
          specialization: member.specialization ?? '',
          skills: member.skills ?? '',
          memberType: member.memberType,
          status: member.status,
          renewalDueDate: member.renewalDueDate ?? '',
          nationalDuesReferenceNo: member.nationalDuesReferenceNo ?? '',
        })
        setApprovedAt(member.approvedAt)
        setIsInGracePeriod(member.isInGracePeriod)
      })
    }
    return Promise.resolve()
  }

  useEffect(() => {
    if (isNew) {
      adminApi.getUsers({ pageSize: 200 }).then((result) => setUsers(result.items))
      return
    }
    load().then(() => setLoading(false))
    if (id) {
      memberApi.getMemberProfileCompleteness(id).then(setCompleteness).catch(() => setCompleteness(null))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, isNew])

  const handleApprove = async () => {
    if (!id) return
    await memberApi.approveMember(id)
    await load()
  }

  const handleChange = <K extends keyof MemberFormState>(field: K, value: MemberFormState[K]) => {
    setState((current) => ({ ...current, [field]: value }))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const yearsOfPractice = state.yearsOfPractice !== '' ? Number(state.yearsOfPractice) : null
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
        civilStatus: state.civilStatus || null,
        address: state.address || null,
        mobileNumber: state.mobileNumber || null,
        housePhone: state.housePhone || null,
        website: state.website || null,
        facebookUrl: state.facebookUrl || null,
        linkedInUrl: state.linkedInUrl || null,
        xUrl: state.xUrl || null,
        instagramUrl: state.instagramUrl || null,
        prcLicenseNo: state.prcLicenseNo || null,
        ptrNumber: state.ptrNumber || null,
        tin: state.tin || null,
        chapter: state.chapter,
        employmentStatus: state.employmentStatus || null,
        company: state.company || null,
        position: state.position || null,
        businessAddress: state.businessAddress || null,
        yearsOfPractice,
        specialization: state.specialization || null,
        skills: state.skills || null,
        memberType: state.memberType,
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
        civilStatus: state.civilStatus || null,
        address: state.address || null,
        mobileNumber: state.mobileNumber || null,
        housePhone: state.housePhone || null,
        website: state.website || null,
        facebookUrl: state.facebookUrl || null,
        linkedInUrl: state.linkedInUrl || null,
        xUrl: state.xUrl || null,
        instagramUrl: state.instagramUrl || null,
        prcLicenseNo: state.prcLicenseNo || null,
        ptrNumber: state.ptrNumber || null,
        tin: state.tin || null,
        chapter: state.chapter,
        employmentStatus: state.employmentStatus || null,
        company: state.company || null,
        position: state.position || null,
        businessAddress: state.businessAddress || null,
        yearsOfPractice,
        specialization: state.specialization || null,
        skills: state.skills || null,
        memberType: state.memberType,
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
          <MemberFormCard
            isNew={isNew}
            state={state}
            onChange={handleChange}
            onSubmit={handleSubmit}
            users={users}
            approvedAt={approvedAt}
            onApprove={handleApprove}
            isInGracePeriod={isInGracePeriod}
            completeness={completeness}
          />
        )}
      </main>
    </>
  )
}
