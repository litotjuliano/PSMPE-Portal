using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Members;

public interface IMemberUploadService
{
    Task<Result> UploadAsync(
        Guid userId, UploadKind kind, Stream content, string fileName, long contentLength, CancellationToken cancellationToken = default);

    Task<(Stream Content, string ContentType)?> GetAsync(Guid userId, UploadKind kind, CancellationToken cancellationToken = default);

    /// <summary>Removes every upload (row + backing file) for this user - used when deleting the
    /// user's login account entirely, since MemberUploads has no FK relationship and would
    /// otherwise be silently orphaned once the account (and its cascaded Member row) is gone.</summary>
    Task DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
