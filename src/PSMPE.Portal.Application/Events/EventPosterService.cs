using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Enums;
using SkiaSharp;

namespace PSMPE.Portal.Application.Events;

/// <summary>
/// Validates, downscales, and stores an Event's poster/banner image via IFileStorageService,
/// writing the resulting key directly onto Event.PosterImageStorageKey - same
/// validate-downscale-reencode-via-SkiaSharp pipeline MemberUploadService uses for Member Photo
/// (src/PSMPE.Portal.Application/Members/MemberUploadService.cs), simplified since a poster has
/// exactly one allowed kind (image, no PDF) and lives directly on the owning row rather than a
/// separate MemberUpload-style join table.
/// </summary>
public class EventPosterService(IApplicationDbContext db, IFileStorageService storage) : IEventPosterService
{
    private const long MaxPosterSizeBytes = 8 * 1024 * 1024;
    private const int MaxPosterDimension = 1600;
    private const int JpegQuality = 82;
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png"];

    public async Task<Result> UploadAsync(
        Guid eventId, Stream content, string fileName, long contentLength, CancellationToken cancellationToken = default)
    {
        var @event = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (@event is null)
        {
            return Result.NotFound($"Event '{eventId}' was not found.");
        }

        if (contentLength == 0)
        {
            return Result.Failure("No file was provided.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return Result.Failure("Only JPG or PNG files are allowed.");
        }

        if (contentLength > MaxPosterSizeBytes)
        {
            return Result.Failure("File exceeds the 8 MB size limit.");
        }

        using var original = SKBitmap.Decode(content);
        if (original is null)
        {
            return Result.Failure("Could not read the image file - it may be corrupted.");
        }

        using var optimized = OptimizeImage(original);
        using var optimizedImage = SKImage.FromBitmap(optimized);
        using var jpegData = optimizedImage.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

        var storageKey = $"events/{eventId}/poster.jpg";
        using var jpegStream = jpegData.AsStream();
        await storage.SaveAsync(storageKey, jpegStream, cancellationToken);

        @event.PosterImageStorageKey = storageKey;
        @event.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<(Stream Content, string ContentType)?> GetAsync(
        Guid eventId, bool includeDrafts = false, CancellationToken cancellationToken = default)
    {
        var @event = await db.Events.AsNoTracking()
            .Where(e => e.Id == eventId)
            .Select(e => new { e.PosterImageStorageKey, e.Status })
            .FirstOrDefaultAsync(cancellationToken);
        // A Draft event's poster is invisible to non-staff too - same "acts like it doesn't exist"
        // treatment as EventService.GetByIdAsync/RegisterAsync.
        if (@event is null || @event.PosterImageStorageKey is null || (!includeDrafts && @event.Status != EventStatus.Published))
        {
            return null;
        }
        var storageKey = @event.PosterImageStorageKey;

        var stream = await storage.OpenReadAsync(storageKey, cancellationToken);
        return stream is null ? null : (stream, "image/jpeg");
    }

    /// <summary>Downscales only (never upscales) so the longest side is at most MaxPosterDimension -
    /// same reasoning as MemberUploadService.OptimizeImage.</summary>
    private static SKBitmap OptimizeImage(SKBitmap original)
    {
        var longestSide = Math.Max(original.Width, original.Height);
        if (longestSide <= MaxPosterDimension)
        {
            return original.Copy();
        }

        var scale = (double)MaxPosterDimension / longestSide;
        var newWidth = (int)Math.Round(original.Width * scale);
        var newHeight = (int)Math.Round(original.Height * scale);

        var resized = original.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.High);
        return resized ?? original.Copy();
    }
}
