import { useEffect, useState, type FormEvent } from 'react'
import { memberApi } from '../api/endpoints/memberApi'
import type { Member } from '../types/member'
import { MyProfileCard, type MyProfileFormState, PageBreadcrumb, PageMeta } from '../../integrations/template'

const emptyState: MyProfileFormState = {
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
}

function toFormState(member: Member): MyProfileFormState {
  return {
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
  }
}

export function MyProfilePage() {
  const [existing, setExisting] = useState<Member | null>(null)
  const [state, setState] = useState<MyProfileFormState>(emptyState)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    memberApi
      .getMyProfile()
      .then((member) => {
        setExisting(member)
        setState(toFormState(member))
      })
      .catch(() => {
        // No profile yet - stay on the empty "complete your profile" form.
        setExisting(null)
      })
      .finally(() => setLoading(false))
  }, [])

  const handleChange = <K extends keyof MyProfileFormState>(field: K, value: MyProfileFormState[K]) => {
    setState((current) => ({ ...current, [field]: value }))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const updated = await memberApi.updateMyProfile({
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
    })
    setExisting(updated)
    setState(toFormState(updated))
  }

  return (
    <>
      <PageMeta title="My Profile" />
      <main>
        <PageBreadcrumb title="My Profile" />
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <MyProfileCard existing={existing} state={state} onChange={handleChange} onSubmit={handleSubmit} />
        )}
      </main>
    </>
  )
}
