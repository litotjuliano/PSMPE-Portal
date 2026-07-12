using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Members;

public interface IMemberUploadService
{
    Task<Result> UploadAsync(
        Guid userId, UploadKind kind, Stream content, string fileName, long contentLength, CancellationToken cancellationToken = default);

    Task<(Stream Content, string ContentType)?> GetAsync(Guid userId, UploadKind kind, CancellationToken cancellationToken = default);
}
