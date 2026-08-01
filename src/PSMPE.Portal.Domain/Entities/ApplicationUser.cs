using Microsoft.AspNetCore.Identity;

namespace PSMPE.Portal.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the account holder gave data privacy consent (RA 10173) at sign-up. Null for accounts
    /// that never went through public registration - seeded accounts and admin-created ones
    /// (<see cref="ApplicationUser"/> rows made by AdminController), plus anyone who registered
    /// before consent was recorded. Null therefore means "no consent on record", not "refused".
    /// </summary>
    public DateTimeOffset? DataPrivacyConsentAt { get; set; }

    /// <summary>
    /// Which revision of the consent wording was accepted, so a later change to the text doesn't
    /// silently reinterpret past consent as agreement to the new terms. Paired with
    /// <see cref="DataPrivacyConsentAt"/> - both set together or both null.
    /// </summary>
    public string? DataPrivacyConsentVersion { get; set; }
}
