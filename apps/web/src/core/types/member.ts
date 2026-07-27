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

export const EducationLevels = {
  TechnicalSchool: 'Technical School',
  CollegeUniversity: 'College / University',
} as const

export type EducationLevelValue = (typeof EducationLevels)[keyof typeof EducationLevels]

export const SpecifiedProfessions = {
  MasterPlumber: 'Master Plumber',
  OtherProfessionalRelated: 'Other Professional Related',
} as const

export type SpecifiedProfessionValue = (typeof SpecifiedProfessions)[keyof typeof SpecifiedProfessions]

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
  educationLevel: string | null
  schoolName: string | null
  courseYearGraduated: string | null
  specifiedProfession: string | null
  mobileNumber: string | null
  houseNo: string | null
  street: string | null
  barangay: string | null
  cityMunicipality: string | null
  province: string | null
  zipCode: string | null
  mailingHouseNo: string | null
  mailingStreet: string | null
  mailingBarangay: string | null
  mailingCityMunicipality: string | null
  mailingProvince: string | null
  mailingZipCode: string | null
  housePhone: string | null
  website: string | null
  facebookUrl: string | null
  linkedInUrl: string | null
  xUrl: string | null
  instagramUrl: string | null
  membershipNo: string
  prcLicenseNo: string | null
  prcRegistrationDate: string | null
  prcValidUntilDate: string | null
  ptrNumber: string | null
  tin: string | null
  prcIdVerified: boolean
  pendingPrcLicenseNo: string | null
  pendingPrcRegistrationDate: string | null
  pendingPrcValidUntilDate: string | null
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
