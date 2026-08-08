import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { memberApi, type ProfileCompleteness } from '../api/endpoints/memberApi'
import { adminApi, type UserSummary } from '../api/endpoints/adminApi'
import { MembershipStatus } from '../types/member'
import type { Member } from '../types/member'
import {
  ApproveApplicationWizard,
  MemberFormCard,
  type MemberFormState,
  PageBreadcrumb,
  PageMeta,
} from '../../integrations/template'

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
  educationLevel: '',
  schoolName: '',
  courseYearGraduated: '',
  specifiedProfession: '',
  houseNo: '',
  street: '',
  barangay: '',
  cityMunicipality: '',
  province: '',
  zipCode: '',
  country: 'Philippines',
  mailingHouseNo: '',
  mailingStreet: '',
  mailingBarangay: '',
  mailingCityMunicipality: '',
  mailingProvince: '',
  mailingZipCode: '',
  mailingCountry: '',
  mobileNumber: '',
  housePhone: '',
  prcLicenseNo: '',
  prcRegistrationDate: '',
  prcValidUntilDate: '',
  ptrNumber: '',
  ptrPlaceIssued: '',
  ptrDateIssued: '',
  tin: '',
  chapter: '',
  chapterYear: '',
  chapterPosition: '',
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
  // Kept alongside the form state because the approval wizard needs the real record - it reads the
  // RMP licence, its pending value and the verification flag, none of which MemberFormState holds.
  const [member, setMember] = useState<Member | null>(null)

  const load = () => {
    if (id) {
      return memberApi.getMemberById(id).then((member) => {
        setMember(member)
        setState({
          userId: member.userId,
          membershipNo: member.membershipNo ?? '',
          firstName: member.firstName,
          middleName: member.middleName ?? '',
          lastName: member.lastName,
          suffix: member.suffix ?? '',
          birthdate: member.birthdate ?? '',
          gender: member.gender ?? '',
          civilStatus: member.civilStatus ?? '',
          educationLevel: member.educationLevel ?? '',
          schoolName: member.schoolName ?? '',
          courseYearGraduated: member.courseYearGraduated ?? '',
          specifiedProfession: member.specifiedProfession ?? '',
          houseNo: member.houseNo ?? '',
          street: member.street ?? '',
          barangay: member.barangay ?? '',
          cityMunicipality: member.cityMunicipality ?? '',
          province: member.province ?? '',
          zipCode: member.zipCode ?? '',
          country: member.country ?? 'Philippines',
          mailingHouseNo: member.mailingHouseNo ?? '',
          mailingStreet: member.mailingStreet ?? '',
          mailingBarangay: member.mailingBarangay ?? '',
          mailingCityMunicipality: member.mailingCityMunicipality ?? '',
          mailingProvince: member.mailingProvince ?? '',
          mailingZipCode: member.mailingZipCode ?? '',
          mailingCountry: member.mailingCountry ?? '',
          mobileNumber: member.mobileNumber ?? '',
          housePhone: member.housePhone ?? '',
          prcLicenseNo: member.prcLicenseNo ?? '',
          prcRegistrationDate: member.prcRegistrationDate ?? '',
          prcValidUntilDate: member.prcValidUntilDate ?? '',
          ptrNumber: member.ptrNumber ?? '',
          ptrPlaceIssued: member.ptrPlaceIssued ?? '',
          ptrDateIssued: member.ptrDateIssued ?? '',
          tin: member.tin ?? '',
          chapter: member.chapter,
          chapterYear: member.chapterYear !== null ? String(member.chapterYear) : '',
          chapterPosition: member.chapterPosition ?? '',
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

  // Opens the review wizard rather than approving directly - the RMP licence has to be verified
  // first, and PSMPE's control number is mandatory and never generated by the portal.
  const [approveOpen, setApproveOpen] = useState(false)

  const handleChange = <K extends keyof MemberFormState>(field: K, value: MemberFormState[K]) => {
    setState((current) => ({ ...current, [field]: value }))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const yearsOfPractice = state.yearsOfPractice !== '' ? Number(state.yearsOfPractice) : null
    if (isNew) {
      await memberApi.createMember({
        userId: state.userId,
        membershipNo: state.membershipNo || null,
        firstName: state.firstName,
        middleName: state.middleName || null,
        lastName: state.lastName,
        suffix: state.suffix || null,
        birthdate: state.birthdate || null,
        gender: state.gender || null,
        civilStatus: state.civilStatus || null,
        educationLevel: state.educationLevel || null,
        schoolName: state.schoolName || null,
        courseYearGraduated: state.courseYearGraduated || null,
        specifiedProfession: state.specifiedProfession || null,
        houseNo: state.houseNo || null,
        street: state.street || null,
        barangay: state.barangay || null,
        cityMunicipality: state.cityMunicipality || null,
        province: state.province || null,
        zipCode: state.zipCode || null,
        country: state.country || null,
        mailingHouseNo: state.mailingHouseNo || null,
        mailingStreet: state.mailingStreet || null,
        mailingBarangay: state.mailingBarangay || null,
        mailingCityMunicipality: state.mailingCityMunicipality || null,
        mailingProvince: state.mailingProvince || null,
        mailingZipCode: state.mailingZipCode || null,
        mailingCountry: state.mailingCountry || null,
        mobileNumber: state.mobileNumber || null,
        housePhone: state.housePhone || null,
        prcLicenseNo: state.prcLicenseNo || null,
        prcRegistrationDate: state.prcRegistrationDate || null,
        prcValidUntilDate: state.prcValidUntilDate || null,
        ptrNumber: state.ptrNumber || null,
        ptrPlaceIssued: state.ptrPlaceIssued || null,
        ptrDateIssued: state.ptrDateIssued || null,
        tin: state.tin || null,
        chapter: state.chapter,
        chapterYear: state.chapterYear !== '' ? Number(state.chapterYear) : null,
        chapterPosition: state.chapterPosition || null,
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
        membershipNo: state.membershipNo || null,
        firstName: state.firstName,
        middleName: state.middleName || null,
        lastName: state.lastName,
        suffix: state.suffix || null,
        birthdate: state.birthdate || null,
        gender: state.gender || null,
        civilStatus: state.civilStatus || null,
        educationLevel: state.educationLevel || null,
        schoolName: state.schoolName || null,
        courseYearGraduated: state.courseYearGraduated || null,
        specifiedProfession: state.specifiedProfession || null,
        houseNo: state.houseNo || null,
        street: state.street || null,
        barangay: state.barangay || null,
        cityMunicipality: state.cityMunicipality || null,
        province: state.province || null,
        zipCode: state.zipCode || null,
        country: state.country || null,
        mailingHouseNo: state.mailingHouseNo || null,
        mailingStreet: state.mailingStreet || null,
        mailingBarangay: state.mailingBarangay || null,
        mailingCityMunicipality: state.mailingCityMunicipality || null,
        mailingProvince: state.mailingProvince || null,
        mailingZipCode: state.mailingZipCode || null,
        mailingCountry: state.mailingCountry || null,
        mobileNumber: state.mobileNumber || null,
        housePhone: state.housePhone || null,
        prcLicenseNo: state.prcLicenseNo || null,
        prcRegistrationDate: state.prcRegistrationDate || null,
        prcValidUntilDate: state.prcValidUntilDate || null,
        ptrNumber: state.ptrNumber || null,
        ptrPlaceIssued: state.ptrPlaceIssued || null,
        ptrDateIssued: state.ptrDateIssued || null,
        tin: state.tin || null,
        chapter: state.chapter,
        chapterYear: state.chapterYear !== '' ? Number(state.chapterYear) : null,
        chapterPosition: state.chapterPosition || null,
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

  // Reaching this page for an existing, not-yet-approved application is a review/approve action,
  // not a routine edit - the breadcrumb/title reflect that distinction (see MemberFormCard, which
  // similarly opens in read-only view mode rather than the editable form for this same reason).
  const pageTitle = isNew ? 'New member' : approvedAt ? 'Edit member' : 'Approval'

  return (
    <>
      <PageMeta title={pageTitle} />
      <main>
        <PageBreadcrumb title={pageTitle} subtitle="Members" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <MemberFormCard
            isNew={isNew}
            memberId={id}
            state={state}
            onChange={handleChange}
            onSubmit={handleSubmit}
            users={users}
            approvedAt={approvedAt}
            onApprove={() => setApproveOpen(true)}
            isInGracePeriod={isInGracePeriod}
            completeness={completeness}
          />
        )}
        <ApproveApplicationWizard
          member={approveOpen ? member : null}
          onApproved={load}
          onCancel={() => setApproveOpen(false)}
        />
      </main>
    </>
  )
}
