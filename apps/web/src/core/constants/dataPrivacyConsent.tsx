/**
 * The one copy of the data privacy consent wording (RA 10173), shared by the sign-up form and the
 * re-consent gate. Kept in a single place deliberately: the backend stamps a single version string
 * onto whoever accepts, so two drifting copies would record the same version against two different
 * texts and make the audit trail meaningless.
 *
 * If this wording changes, bump `DataPrivacyConsent.CurrentVersion` in
 * `src/PSMPE.Portal.Domain/Enums/DataPrivacyConsent.cs` in the same change - that is what asks
 * every existing account to re-consent instead of silently treating old consent as agreement to
 * the new terms.
 */
export const DATA_PRIVACY_ACT_URL = 'https://privacy.gov.ph/data-privacy-act/'

export const DataPrivacyConsentText = () => (
  <>
    <span className="font-semibold text-default-900">DATA PRIVACY CONSENT:</span> I agree that my personal information
    may be collected, processed, stored, and maintained by the Association. In digital, electronic, and/or printed form.
    My personal information shall be kept confidential and used solely for legitimate organizational purposes in
    accordance with the{' '}
    <a
      href={DATA_PRIVACY_ACT_URL}
      target="_blank"
      rel="noopener noreferrer"
      className="font-semibold text-primary underline"
    >
      Data Privacy Act of 2012 (Republic Act No. 10173)
    </a>
    .
  </>
)
