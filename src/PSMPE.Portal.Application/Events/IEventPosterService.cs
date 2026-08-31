using PSMPE.Portal.Application.Common.Models;

namespace PSMPE.Portal.Application.Events;

public interface IEventPosterService
{
    Task<Result> UploadAsync(
        Guid eventId, Stream content, string fileName, long contentLength, CancellationToken cancellationToken = default);

    Task<(Stream Content, string ContentType)?> GetAsync(Guid eventId, CancellationToken cancellationToken = default);
}
