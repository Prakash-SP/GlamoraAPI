using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeachyGlamora.Api.Services;

namespace PeachyGlamora.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/media")]
[Authorize(Roles = "Admin")]
public class MediaController : ControllerBase
{
    private readonly ICloudinaryService _cloudinary;
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private const long MaxFileSizeBytes = 8 * 1024 * 1024; // 8 MB

    public MediaController(ICloudinaryService cloudinary) => _cloudinary = cloudinary;

    /// <summary>Upload a single image (product photo, category banner, blog cover, etc.).
    /// The frontend admin panel calls this first, then passes the returned URL into
    /// POST /api/admin/products/{id}/images or wherever the image is being attached.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] string folder = "products")
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file was uploaded." });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { error = "File too large — maximum size is 8MB." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { error = "Only JPG, PNG, and WEBP images are allowed." });

        // Restrict which subfolders the admin panel can write to, rather than trusting an
        // arbitrary client-supplied path.
        var safeFolder = folder switch
        {
            "products" or "categories" or "blog" or "banners" => $"peachy-glamora/{folder}",
            _ => "peachy-glamora/misc"
        };

        await using var stream = file.OpenReadStream();
        var result = await _cloudinary.UploadImageAsync(stream, file.FileName, safeFolder);

        return Ok(new { result.Url, result.PublicId, result.Width, result.Height });
    }

    [HttpDelete("{*publicId}")]
    public async Task<IActionResult> Delete(string publicId)
    {
        await _cloudinary.DeleteImageAsync(Uri.UnescapeDataString(publicId));
        return Ok(new { message = "Image deleted." });
    }
}
