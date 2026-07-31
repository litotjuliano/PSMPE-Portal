using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using SkiaSharp;

namespace PSMPE.Portal.Application.Members;

/// <summary>
/// Validates, optimizes (for images), and stores member-uploaded files (photo, RMP/PRC ID) via
/// IFileStorageService - deliberately decoupled from MemberService/the profile-fields autosave,
/// since a file's owner is always "this user," independent of whether they've saved any Personal
/// Info yet. See openspecs/members.md.
/// </summary>
public class MemberUploadService(IApplicationDbContext db, IFileStorageService storage) : IMemberUploadService
{
    private const long MaxPdfSizeBytes = 2 * 1024 * 1024;
    private const long MaxRawImageSizeBytes = 24 * 1024 * 1024;
    // Proof of Payment gets its own, much smaller cap per the application form - applies
    // regardless of whether the upload is an image or a PDF.
    private const long MaxProofOfPaymentSizeBytes = 1 * 1024 * 1024;
    private const int MaxImageDimension = 1600;
    private const int JpegQuality = 82;

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png"];
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".pdf"];
    private static readonly UploadKind[] PdfAllowedKinds = [UploadKind.PrcId, UploadKind.ProofOfPayment];

    // Only these two kinds get the "surname-firstname-birthdate-timestamp" filename convention
    // from the application form (Photo - also used for ID printing, and Proof of Payment) - other
    // kinds keep the plain "{kind}.ext" key. Purely a storage-key naming choice - re-upload/
    // overwrite behavior (looked up by (userId, kind) in MemberUploads) is unchanged either way.
    private static readonly UploadKind[] NamedFileKinds = [UploadKind.Photo, UploadKind.ProofOfPayment];

    public async Task<Result> UploadAsync(
        Guid userId, UploadKind kind, Stream content, string fileName, long contentLength, CancellationToken cancellationToken = default)
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

        if (extension == ".pdf" && !PdfAllowedKinds.Contains(kind))
        {
            return Result.Failure("Only the RMP ID and Proof of Payment uploads accept PDF files.");
        }

        var isImage = ImageExtensions.Contains(extension);
        var maxSize = kind == UploadKind.ProofOfPayment ? MaxProofOfPaymentSizeBytes : isImage ? MaxRawImageSizeBytes : MaxPdfSizeBytes;
        if (contentLength > maxSize)
        {
            var limitDescription = kind == UploadKind.ProofOfPayment ? "1 MB" : isImage ? "24 MB" : "2 MB";
            return Result.Failure($"File exceeds the {limitDescription} size limit.");
        }

        var fileNamePrefix = NamedFileKinds.Contains(kind)
            ? await BuildFileNamePrefixAsync(userId, kind, cancellationToken)
            : kind.ToString().ToLowerInvariant();

        string storageKey;
        string contentType;

        if (isImage)
        {
            using var original = SKBitmap.Decode(content);
            if (original is null)
            {
                return Result.Failure("Could not read the image file - it may be corrupted.");
            }

            using var optimized = OptimizeImage(original);
            using var optimizedImage = SKImage.FromBitmap(optimized);
            using var jpegData = optimizedImage.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

            storageKey = $"{userId}/{fileNamePrefix}.jpg";
            contentType = "image/jpeg";
            using var jpegStream = jpegData.AsStream();
            await storage.SaveAsync(storageKey, jpegStream, cancellationToken);
        }
        else
        {
            storageKey = $"{userId}/{fileNamePrefix}.pdf";
            contentType = "application/pdf";
            await storage.SaveAsync(storageKey, content, cancellationToken);
        }

        var existing = await db.MemberUploads.FirstOrDefaultAsync(u => u.UserId == userId && u.Kind == kind, cancellationToken);
        if (existing is null)
        {
            db.MemberUploads.Add(new MemberUpload
            {
                UserId = userId,
                Kind = kind,
                StorageKey = storageKey,
                ContentType = contentType
            });
        }
        else
        {
            existing.StorageKey = storageKey;
            existing.ContentType = contentType;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Builds the "{kind}-{surname}-{firstname}-{birthdate}-{timestamp}" prefix the application
    /// form specifies for the Photo and Proof of Payment uploads (e.g. renaming a re-upload
    /// the same way as the first). Falls back gracefully if the Member row or its name/birthdate
    /// aren't available yet (e.g. a very early wizard step) - this is a cosmetic storage-key
    /// convenience, never a hard requirement to upload.
    /// </summary>
    private async Task<string> BuildFileNamePrefixAsync(Guid userId, UploadKind kind, CancellationToken cancellationToken)
    {
        var member = await db.Members.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.LastName, m.FirstName, m.Birthdate })
            .FirstOrDefaultAsync(cancellationToken);

        var surname = Sanitize(member?.LastName);
        var firstName = Sanitize(member?.FirstName);
        var birthdate = member?.Birthdate?.ToString("yyyyMMdd") ?? "unknown";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return $"{kind.ToString().ToLowerInvariant()}-{surname}-{firstName}-{birthdate}-{timestamp}";
    }

    private static readonly Regex NonAlphanumeric = new("[^A-Za-z0-9]+", RegexOptions.Compiled);

    private static string Sanitize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : NonAlphanumeric.Replace(value.Trim(), "").ToLowerInvariant();

    public async Task<(Stream Content, string ContentType)?> GetAsync(
        Guid userId, UploadKind kind, CancellationToken cancellationToken = default)
    {
        var upload = await db.MemberUploads.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId && u.Kind == kind, cancellationToken);
        if (upload is null)
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(upload.StorageKey, cancellationToken);
        return stream is null ? null : (stream, upload.ContentType);
    }

    public async Task DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var uploads = await db.MemberUploads.Where(u => u.UserId == userId).ToListAsync(cancellationToken);
        foreach (var upload in uploads)
        {
            await storage.DeleteAsync(upload.StorageKey, cancellationToken);
        }

        db.MemberUploads.RemoveRange(uploads);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Downscales only (never upscales) so the longest side is at most MaxImageDimension.</summary>
    private static SKBitmap OptimizeImage(SKBitmap original)
    {
        var longestSide = Math.Max(original.Width, original.Height);
        if (longestSide <= MaxImageDimension)
        {
            return original.Copy();
        }

        var scale = (double)MaxImageDimension / longestSide;
        var newWidth = (int)Math.Round(original.Width * scale);
        var newHeight = (int)Math.Round(original.Height * scale);

        var resized = original.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.High);
        return resized ?? original.Copy();
    }
}
