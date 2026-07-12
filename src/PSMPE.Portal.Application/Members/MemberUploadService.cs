using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using SkiaSharp;

namespace PSMPE.Portal.Application.Members;

/// <summary>
/// Validates, optimizes (for images), and stores member-uploaded files (photo, PRC ID) via
/// IFileStorageService - deliberately decoupled from MemberService/the profile-fields autosave,
/// since a file's owner is always "this user," independent of whether they've saved any Personal
/// Info yet. See openspecs/members.md.
/// </summary>
public class MemberUploadService(IApplicationDbContext db, IFileStorageService storage) : IMemberUploadService
{
    private const long MaxPdfSizeBytes = 2 * 1024 * 1024;
    private const long MaxRawImageSizeBytes = 24 * 1024 * 1024;
    private const int MaxImageDimension = 1600;
    private const int JpegQuality = 82;

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png"];
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".pdf"];

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

        if (extension == ".pdf" && kind != UploadKind.PrcId)
        {
            return Result.Failure("Only the PRC ID upload accepts PDF files.");
        }

        var isImage = ImageExtensions.Contains(extension);
        var maxSize = isImage ? MaxRawImageSizeBytes : MaxPdfSizeBytes;
        if (contentLength > maxSize)
        {
            var limitDescription = isImage ? "24 MB" : "2 MB";
            return Result.Failure($"File exceeds the {limitDescription} size limit.");
        }

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

            storageKey = $"{userId}/{kind.ToString().ToLowerInvariant()}.jpg";
            contentType = "image/jpeg";
            using var jpegStream = jpegData.AsStream();
            await storage.SaveAsync(storageKey, jpegStream, cancellationToken);
        }
        else
        {
            storageKey = $"{userId}/{kind.ToString().ToLowerInvariant()}.pdf";
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
