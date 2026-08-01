namespace PSMPE.Portal.Domain.Enums;

public static class DataPrivacyConsent
{
    /// <summary>
    /// Revision of the data privacy consent wording (RA 10173) currently shown to users. Stamped
    /// onto <see cref="Entities.ApplicationUser.DataPrivacyConsentVersion"/> at sign-up, and
    /// compared against it afterwards to decide whether an account still holds current consent.
    ///
    /// <para><b>Bump this whenever the consent text in <c>RegisterPage.tsx</c> /
    /// <c>DataPrivacyConsentGate.tsx</c> changes.</b> Doing so makes every existing account's
    /// consent stale, so they are asked to re-consent on their next visit - which is the point:
    /// without it, a wording change would silently pass off old consent as agreement to new
    /// terms. Dated rather than sequential so it is obvious *when* the wording changed.</para>
    /// </summary>
    public const string CurrentVersion = "2026-08-01";

    /// <summary>
    /// True when an account does not hold consent at <see cref="CurrentVersion"/>. Null (never
    /// consented - seeded and admin-created accounts, or anyone who registered before consent was
    /// recorded) counts as needing consent: it means "no consent on record", not "refused".
    /// </summary>
    public static bool NeedsConsent(string? consentedVersion) => consentedVersion != CurrentVersion;
}
