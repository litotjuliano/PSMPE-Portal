using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Payments.Dtos;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Payments;

public interface IPaymentService
{
    /// <summary>Admin queue - defaults to Submitted, oldest first.</summary>
    Task<PagedResult<PaymentDto>> GetAllAsync(
        int page, int pageSize, PaymentStatus? status = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentDto>> GetForMemberAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<PaymentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The raw storage key for a payment's proof. Deliberately not on PaymentDto - the key embeds
    /// the member's surname, first name and birthdate (see MemberUploadService's naming), so it
    /// stays server-side and only the file bytes are ever served.
    /// </summary>
    Task<string?> GetProofKeyAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Self-service submission. Determines <see cref="PaymentKind"/> from the member's own state
    /// rather than trusting the caller, and refuses a second submission while one is already
    /// awaiting a decision.
    /// </summary>
    Task<Result<PaymentDto>> SubmitAsync(
        Guid userId, SubmitPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Attaches or replaces the proof document for the caller's own pending payment.</summary>
    Task<Result> AttachProofAsync(
        Guid paymentId, string storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The only thing that moves a member to Active or advances RenewalDueDate. See the
    /// implementation for the due-date arithmetic, which differs by kind.
    /// </summary>
    Task<Result> VerifyAsync(Guid paymentId, Guid decidedByUserId, CancellationToken cancellationToken = default);

    Task<Result> RejectAsync(Guid paymentId, string reason, Guid decidedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Self-service submission of a proof-of-payment for an event registration - the
    /// EventRegistration counterpart to SubmitAsync. Kind is always EventRegistration, decided by
    /// the caller passing a registrationId rather than trusted from the request body.</summary>
    Task<Result<PaymentDto>> SubmitForEventRegistrationAsync(
        Guid userId, Guid registrationId, SubmitPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Creates and immediately verifies a Payment with no proof file, for an on-site cash
    /// payer - reaches the same PaymentVerified state as the proof-upload path in one call. Refused
    /// if the registration already has a Submitted or Verified Payment.</summary>
    Task<Result<PaymentDto>> RecordEventCashPaymentAsync(
        Guid registrationId, decimal amount, Guid decidedByUserId, CancellationToken cancellationToken = default);

    Task<MembershipFeesDto> GetFeesAsync(CancellationToken cancellationToken = default);

    Task<Result> UpdateFeesAsync(UpdateMembershipFeesRequest request, CancellationToken cancellationToken = default);

    /// <summary>All configured promotions, newest-starting first - the admin configuration list, not
    /// the resolved price a member sees.</summary>
    Task<IReadOnlyList<FeePromotionDto>> GetPromotionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Rejects an unrecognized FeeKey, an inverted date range, or one overlapping an
    /// existing promotion for the same FeeKey.</summary>
    Task<Result<FeePromotionDto>> CreatePromotionAsync(
        CreateFeePromotionRequest request, Guid createdByUserId, CancellationToken cancellationToken = default);

    /// <summary>Hard delete - a promotion is a lightweight schedule, not an audited record; already-
    /// created Payments captured their own amount independently.</summary>
    Task<Result> DeletePromotionAsync(Guid id, CancellationToken cancellationToken = default);
}
