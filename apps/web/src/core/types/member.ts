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

export const CivilStatuses = {
  Single: 'Single',
  Married: 'Married',
  Widowed: 'Widowed',
  Separated: 'Separated',
  Annulled: 'Annulled',
} as const

export type CivilStatusValue = (typeof CivilStatuses)[keyof typeof CivilStatuses]

export const EmploymentStatuses = {
  Employed: 'Employed',
  SelfEmployed: 'Self-Employed',
  BusinessOwner: 'Business Owner',
  Student: 'Student',
  Retired: 'Retired',
  Unemployed: 'Unemployed',
} as const

export type EmploymentStatusValue = (typeof EmploymentStatuses)[keyof typeof EmploymentStatuses]

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
  civilStatus: string | null
  address: string | null
  mobileNumber: string | null
  housePhone: string | null
  website: string | null
  facebookUrl: string | null
  linkedInUrl: string | null
  xUrl: string | null
  instagramUrl: string | null
  membershipNo: string
  prcLicenseNo: string | null
  ptrNumber: string | null
  tin: string | null
  prcIdVerified: boolean
  pendingPrcLicenseNo: string | null
  prcVerificationRejectedReason: string | null
  chapter: string
  employmentStatus: string | null
  company: string | null
  position: string | null
  businessAddress: string | null
  yearsOfPractice: number | null
  specialization: string | null
  skills: string | null
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
