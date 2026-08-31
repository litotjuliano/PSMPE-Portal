export const MembershipStatus = {
  Pending: 'Pending',
  Active: 'Active',
  Expired: 'Expired',
  Deactivated: 'Deactivated',
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

/** Mirrors MemberTypes.cs. Regular stays first because every member predating the other three
 *  carries it - dropping it would leave those records showing a value the dropdown doesn't offer. */
export const MemberTypes = {
  Regular: 'Regular Member',
  New: 'New',
  Renewal: 'Renewal',
  SeniorCitizen: 'Senior Citizen',
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
  country: string | null
  mailingHouseNo: string | null
  mailingStreet: string | null
  mailingBarangay: string | null
  mailingCityMunicipality: string | null
  mailingProvince: string | null
  mailingZipCode: string | null
  mailingCountry: string | null
  housePhone: string | null
  /** Null until an admin assigns PSMPE's control number at approval. */
  membershipNo: string | null
  prcLicenseNo: string | null
  prcRegistrationDate: string | null
  prcValidUntilDate: string | null
  ptrNumber: string | null
  ptrPlaceIssued: string | null
  ptrDateIssued: string | null
  tin: string | null
  prcIdVerified: boolean
  pendingPrcLicenseNo: string | null
  pendingPrcRegistrationDate: string | null
  pendingPrcValidUntilDate: string | null
  prcVerificationRejectedReason: string | null
  chapter: string
  /** Optional chapter officer post - the year held and the role. Distinct from `position`, which
   *  is the member's employment position. */
  chapterYear: number | null
  chapterPosition: string | null
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
  /** Whether the member currently has portal access. Reflects only the most recently verified
   *  payment - written server-side exclusively by PaymentVerification.Apply, never a direct toggle. */
  hasPortalAccess: boolean
  nationalDuesReferenceNo: string | null
  approvedAt: string | null
  submittedAt: string | null
  isInGracePeriod: boolean
  /** Past the renewal date AND past the grace period. Derived server-side, not stored - there is
   *  no scheduler, so the persisted status stays admin-controlled. */
  isExpired: boolean
  createdAt: string
  updatedAt: string | null
}
