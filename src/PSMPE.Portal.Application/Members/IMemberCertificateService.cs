using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members.Dtos;

namespace PSMPE.Portal.Application.Members;

public interface IMemberCertificateService
{
    Task<Result> UploadAsync(Guid userId, Stream content, string fileName, long contentLength, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemberCertificateDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<(Stream Content, string ContentType, string FileName)?> GetAsync(Guid userId, Guid certificateId, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid userId, Guid certificateId, CancellationToken cancellationToken = default);
}
