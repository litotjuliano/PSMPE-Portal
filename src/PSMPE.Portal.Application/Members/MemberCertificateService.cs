using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Application.Members;

/// <summary>
/// Unlike MemberUploadService (one file per UserId+Kind, always re-encoded to a fixed key),
/// certificates are unbounded per member and stored as-is (no re-encoding) - a certificate is
/// usually a scanned document/PDF a member wants preserved exactly, not a photo needing
/// optimization. Each certificate gets its own randomly-keyed storage path since there's no
/// natural single slot to overwrite.
/// </summary>
public class MemberCertificateService(IApplicationDbContext db, IFileStorageService storage) : IMemberCertificateService
{
    private const long MaxPdfSizeBytes = 2 * 1024 * 1024;
    private const long MaxImageSizeBytes = 24 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".pdf"];

    public async Task<Result> UploadAsync(
        Guid userId, Stream content, string fileName, long contentLength, CancellationToken cancellationToken = default)
    {
        if (contentLength == 0)
        {
            return Result.Failure("No file was provided.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return Result.Failure("Only JPG, PNG, or PDF files are allowed.");
        }

        var isPdf = extension == ".pdf";
        var maxSize = isPdf ? MaxPdfSizeBytes : MaxImageSizeBytes;
        if (contentLength > maxSize)
        {
            var limitDescription = isPdf ? "2 MB" : "24 MB";
            return Result.Failure($"File exceeds the {limitDescription} size limit.");
        }

        var certificateId = Guid.NewGuid();
        var storageKey = $"{userId}/certificates/{certificateId}{extension}";
        var contentType = isPdf ? "application/pdf" : extension is ".png" ? "image/png" : "image/jpeg";
        await storage.SaveAsync(storageKey, content, cancellationToken);

        db.MemberCertificates.Add(new MemberCertificate
        {
            Id = certificateId,
            UserId = userId,
            FileName = Path.GetFileName(fileName),
            StorageKey = storageKey,
            ContentType = contentType,
            FileSizeBytes = contentLength
        });

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyList<MemberCertificateDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var certificates = await db.MemberCertificates.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return certificates.Select(c => new MemberCertificateDto(c.Id, c.FileName, c.ContentType, c.FileSizeBytes, c.CreatedAt)).ToList();
    }

    public async Task<(Stream Content, string ContentType, string FileName)?> GetAsync(
        Guid userId, Guid certificateId, CancellationToken cancellationToken = default)
    {
        var certificate = await db.MemberCertificates.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == certificateId && c.UserId == userId, cancellationToken);
        if (certificate is null)
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(certificate.StorageKey, cancellationToken);
        return stream is null ? null : (stream, certificate.ContentType, certificate.FileName);
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid certificateId, CancellationToken cancellationToken = default)
    {
        var certificate = await db.MemberCertificates.FirstOrDefaultAsync(c => c.Id == certificateId && c.UserId == userId, cancellationToken);
        if (certificate is null)
        {
            return Result.NotFound("Certificate was not found.");
        }

        await storage.DeleteAsync(certificate.StorageKey, cancellationToken);
        db.MemberCertificates.Remove(certificate);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
