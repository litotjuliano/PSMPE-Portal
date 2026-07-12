using Microsoft.AspNetCore.Hosting;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// Dev/local-only IFileStorageService backend - writes/reads under wwwroot/uploads on whatever
/// disk the app happens to be running on. Won't survive a redeploy on a platform with an
/// ephemeral filesystem (see openspecs/members.md's Open Questions) - swap in a real object-store
/// implementation (e.g. DigitalOcean Spaces) before relying on this in production.
/// </summary>
public class LocalDiskFileStorageService(IWebHostEnvironment env) : IFileStorageService
{
    private string UploadsDirectory
    {
        get
        {
            var webRootPath = string.IsNullOrEmpty(env.WebRootPath)
                ? Path.Combine(env.ContentRootPath, "wwwroot")
                : env.WebRootPath;
            return Path.Combine(webRootPath, "uploads");
        }
    }

    public async Task<string> SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(UploadsDirectory);
        var filePath = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await using var fileStream = File.Create(filePath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var filePath = ResolvePath(key);
        if (!File.Exists(filePath))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(File.OpenRead(filePath));
    }

    private string ResolvePath(string key) => Path.Combine(UploadsDirectory, key);
}
