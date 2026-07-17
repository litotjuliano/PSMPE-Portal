namespace PSMPE.Portal.Application.Common.Interfaces;

/// <summary>
/// Storage backend for uploaded files, addressed by an opaque key - deliberately abstracted so
/// swapping local disk (dev) for a real object store (e.g. DigitalOcean Spaces, in production)
/// later is a contained change, not a rewrite. See openspecs/members.md.
/// </summary>
public interface IFileStorageService
{
    Task<string> SaveAsync(string key, Stream content, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
