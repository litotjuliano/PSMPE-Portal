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

export const MemberTypes = {
  Regular: 'Regular Member',
} as const

export type MemberTypeValue = (typeof MemberTypes)[keyof typeof MemberTypes]

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
  prcIdVerified: boolean
  pendingPrcLicenseNo: string | null
  prcVerificationRejectedReason: string | null
  chapter: string
  company: string | null
  memberType: string
  status: MembershipStatusValue
  renewalDueDate: string | null
  nationalDuesReferenceNo: string | null
  approvedAt: string | null
  submittedAt: string | null
  isInGracePeriod: boolean
  createdAt: string
  updatedAt: string | null
}
