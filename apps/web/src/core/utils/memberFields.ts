/**
 * Cross-form helpers for Member fields. The wizard, the profile tabs and the admin member form all
 * write the same entity, so a rule that lives in only one of them is a rule the other two silently
 * skip. Validators here mirror the server-side guards in MemberService - the server is still the
 * source of truth, these only save a round trip on an obvious mistake.
 */

import type { Member } from '../types/member'

export const CHAPTER_YEAR_MIN = 1900
export const CHAPTER_YEAR_MAX = 2200

/**
 * An RMP/PRC licence runs one year from its registration date, so Valid Until is offered as
 * Registration Date + 1 year. Returns '' for an unparseable or blank input.
 *
 * A suggestion, never a lock - `shouldDeriveValidUntil` decides whether it's safe to apply.
 */
export function deriveRmpValidUntil(registrationDate: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(registrationDate)
  if (!match) return ''
  const [, yearPart, monthPart, dayPart] = match

  // Deliberately string math rather than `new Date(...).setFullYear(...)`: parsing a bare date as
  // local time and formatting it back with toISOString() lands a day early everywhere east of UTC,
  // which here would have made every auto-filled Valid Until off by one.
  const year = Number(yearPart) + 1
  // Day 0 of the following month is the last day of this one - clamps a 29 Feb registration to
  // 28 Feb rather than rolling it into March.
  const lastDayOfMonth = new Date(Date.UTC(year, Number(monthPart), 0)).getUTCDate()
  const day = Math.min(Number(dayPart), lastDayOfMonth)

  return `${year}-${monthPart}-${String(day).padStart(2, '0')}`
}

/**
 * Whether a new Registration Date should overwrite the Valid Until currently on screen.
 *
 * Only when Valid Until is blank, or still holds exactly what the *previous* registration date
 * derived - i.e. the applicant never touched it. A date they typed themselves is left alone, since
 * silently replacing it would be worse than making them fix a wrong auto-fill.
 */
export function shouldDeriveValidUntil(currentValidUntil: string, previousRegistrationDate: string): boolean {
  return !currentValidUntil || currentValidUntil === deriveRmpValidUntil(previousRegistrationDate)
}

/**
 * Philippine mobile, formatted live as it's typed. The three forms MemberService accepts
 * (+639XXXXXXXXX / 639XXXXXXXXX / 09XXXXXXXXX) differ only in their prefix, so this keeps a leading
 * `+` when one is typed first, drops every other non-digit, and caps the length - no grouping,
 * matching the ungrouped placeholder.
 */
export function formatPhMobile(raw: string): string {
  const plus = raw.trimStart().startsWith('+') ? '+' : ''
  const digits = raw.replace(/\D/g, '')
  // Cap by prefix, not by the `+`: 639XXXXXXXXX is 12 digits with or without one, while
  // 09XXXXXXXXX is 11. Capping everything at 11 would silently eat the last digit of a
  // country-code number the server would have accepted.
  return plus + digits.slice(0, digits.startsWith('63') ? 12 : 11)
}

/**
 * Philippine landline, formatted live into `(02) 8123 4567` / `(032) 255 1234`.
 *
 * Idempotent - it strips to digits first, so re-running it on its own output is a no-op, which is
 * what makes it safe to call on every keystroke.
 *
 * The area code is inferred from the trunk prefix: `02` is NCR (the only single-digit area code),
 * anything else after the `0` is assumed to be a two-digit area code. That covers the overwhelming
 * majority of PH landlines but is a heuristic, not a lookup table - which is why the field stays
 * free-text and the server validates on digit count (7-11) rather than on this shape.
 */
export function formatPhLandline(raw: string): string {
  const digits = raw.replace(/\D/g, '').slice(0, 11)
  // Without the leading 0 there's no trunk prefix to split on, so don't guess at a grouping.
  if (!digits.startsWith('0')) return digits

  const isNcr = digits.startsWith('02')
  const area = isNcr ? digits.slice(0, 2) : digits.slice(0, 3)
  const rest = digits.slice(area.length)
  if (digits.length <= area.length) return `(${digits}`
  if (rest.length <= 4) return `(${area}) ${rest}`

  // NCR subscriber numbers are always 8 digits, so pin it to 4+4 rather than letting the
  // length-based rule below briefly regroup to 3+4 while the 7th digit is being typed.
  const split = !isNcr && rest.length === 7 ? 3 : 4
  return `(${area}) ${rest.slice(0, split)} ${rest.slice(split)}`
}

export const CHAPTER_YEAR_ERROR = `Chapter year must be between ${CHAPTER_YEAR_MIN} and ${CHAPTER_YEAR_MAX}.`

/**
 * Blank is valid - the field is optional, and callers are expected to skip the check entirely for
 * an empty value. A non-numeric or out-of-range entry is not.
 */
export function isValidChapterYear(value: string): boolean {
  if (!value) return true
  const year = Number(value)
  return Number.isInteger(year) && year >= CHAPTER_YEAR_MIN && year <= CHAPTER_YEAR_MAX
}

/**
 * Whether the "Same as Residence Address" checkbox should start ticked. There's no stored flag for
 * it - the mailing columns are just seven plain fields - so the state has to be inferred:
 *
 * - mailing entirely blank (a fresh or barely-started draft) -> ticked, which is the default the
 *   vast majority of applicants want
 * - mailing already an exact copy of residence -> ticked, since that's what ticking it produces
 * - anything else, including a partial copy -> unticked
 *
 * The last case is the important one. Defaulting to ticked unconditionally would mean a member who
 * deliberately entered a *different* mailing address comes back, sees it ticked, and has that
 * address silently overwritten with their residence on the next save.
 */
export function mailingMirrorsResidence(member: Member): boolean {
  const pairs: [string | null, string | null][] = [
    [member.mailingHouseNo, member.houseNo],
    [member.mailingStreet, member.street],
    [member.mailingBarangay, member.barangay],
    [member.mailingCityMunicipality, member.cityMunicipality],
    [member.mailingProvince, member.province],
    [member.mailingZipCode, member.zipCode],
    [member.mailingCountry, member.country],
  ]

  if (pairs.every(([mailing]) => !mailing)) return true
  return pairs.every(([mailing, residence]) => (mailing ?? '') === (residence ?? ''))
}
