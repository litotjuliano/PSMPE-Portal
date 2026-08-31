using System.Net.Http.Headers;
using SkiaSharp;

namespace PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;

/// <summary>
/// Shared multipart-file-upload helpers for integration tests exercising IFormFile-accepting
/// endpoints (member photo/PRC-ID uploads, the event poster upload). Extracted from
/// MemberUploadsTests.cs/EventsControllerTests.cs, which previously each carried their own copy
/// of the exact same two methods.
/// </summary>
public static class UploadTestHelpers
{
    /// <summary>Encodes a solid-color bitmap of the given dimensions as PNG bytes - a minimal
    /// valid image for upload tests that only care about dimensions/round-tripping, not content.</summary>
    public static byte[] BuildPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Builds a bearer-authenticated multipart/form-data POST carrying a single file
    /// under the "file" field name, matching the [FromForm]/IFormFile binding the upload
    /// controllers expect.</summary>
    public static HttpRequestMessage BuildUploadRequest(string url, string token, byte[] bytes, string fileName, string contentType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        request.Content = content;
        return request;
    }
}
