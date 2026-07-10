export const MembershipStatus = {
  Pending: 0,
  Active: 1,
  Expired: 2,
  Deactivated: 3,
} as const

export type MembershipStatusValue = (typeof MembershipStatus)[keyof typeof MembershipStatus]

export const Chapters = {
  Ncr: 'NCR',
  Cebu: 'Cebu',
  Davao: 'Davao',
  Baguio: 'Baguio',
  Cavite: 'Cavite',
  QuezonCity: 'Quezon City Chapter',
} as const

export type ChapterValue = (typeof Chapters)[keyof typeof Chapters]

export interface Member {
  id: string
  userId: string
  email: string
  firstName: string
  middleName: string | null
  lastName: string
  suffix: string | null
  birthdate: string | null
  gender: string | null
  address: string | null
  membershipNo: string
  prcLicenseNo: string | null
  chapter: string
  company: string | null
  status: MembershipStatusValue
  renewalDueDate: string | null
  nationalDuesReferenceNo: string | null
  photoUrl: string | null
  prcIdUrl: string | null
  createdAt: string
  updatedAt: string | null
}
