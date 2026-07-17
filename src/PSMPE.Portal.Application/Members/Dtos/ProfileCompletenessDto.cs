namespace PSMPE.Portal.Application.Members.Dtos;

/// <summary>
/// Computed on demand (not part of MemberDto/ToDto) - checking 3 upload kinds plus a certificate
/// count isn't cheap enough to run in the loop the paginated Members/Membership Approvals/PRC
/// Verifications list endpoints use. Only ever fetched for a single member at a time (the
/// dashboard's own completeness banner, or the admin single-member detail view).
/// </summary>
public record ProfileCompletenessDto(
    int PercentComplete,
    bool IsSubmitted,
    bool HasPrcId,
    bool HasValidGovernmentId,
    bool HasFormalPhoto,
    bool HasSignature,
    int CertificateCount,
    bool HasProfessionalInfo);
