namespace PSMPE.Portal.Domain.Enums;

public enum UploadKind
{
    Photo,
    PrcId,
    ValidGovernmentId,
    Signature,
    ProofOfPayment,
    /// <summary>System-generated on approval (MembersController.Approve) - never uploaded by the
    /// member themselves. See ReceiptGenerator.</summary>
    Receipt,
}
