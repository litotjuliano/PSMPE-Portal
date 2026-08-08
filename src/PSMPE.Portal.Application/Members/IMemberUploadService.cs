using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Members;

public interface IMemberUploadService
{
    Task<Result> UploadAsync(
        Guid userId, UploadKind kind, Stream content, string fileName, long contentLength, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a payment proof and returns its storage key, writing **no** MemberUpload row.
    ///
    /// Payments own their document (Payment.ProofStorageKey) because MemberUpload is one row per
    /// (UserId, Kind) - a renewal proof would repoint the single ProofOfPayment slot and the
    /// registration proof would become unreachable. Validation and image optimisation are shared
    /// with UploadAsync, so the same limits apply.
    /// </summary>
    Task<Result<string>> UploadPaymentProofAsync(
        Guid userId, Stream content, string fileName, long contentLength, CancellationToken cancellationToken = default);

    Task<(Stream Content, string ContentType)?> GetAsync(Guid userId, UploadKind kind, CancellationToken cancellationToken = default);

    /// <summary>Opens a file by its raw storage key - for documents tracked outside MemberUploads,
    /// i.e. payment proofs.</summary>
    Task<(Stream Content, string ContentType)?> OpenByKeyAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Removes every upload (row + backing file) for this user - used when deleting the
    /// user's login account entirely, since MemberUploads has no FK relationship and would
    /// otherwise be silently orphaned once the account (and its cascaded Member row) is gone.</summary>
    Task DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
