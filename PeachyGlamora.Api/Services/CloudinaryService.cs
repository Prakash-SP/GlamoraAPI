using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace PeachyGlamora.Api.Services;

public record UploadedImage(string Url, string PublicId, int Width, int Height);

public interface ICloudinaryService
{
    Task<UploadedImage> UploadImageAsync(Stream fileStream, string fileName, string folder);
    Task DeleteImageAsync(string publicId);
}

/// <summary>Thin wrapper over CloudinaryDotNet so controllers never touch the SDK directly —
/// keeps the option open to swap storage providers (S3 + CloudFront, etc.) later.</summary>
public class CloudinaryService : ICloudinaryService
{
    private readonly Cloudinary _cloudinary;

    public CloudinaryService(IConfiguration config)
    {
        var account = new Account(
            config["Cloudinary:CloudName"],
            config["Cloudinary:ApiKey"],
            config["Cloudinary:ApiSecret"]);
        _cloudinary = new Cloudinary(account);
    }

    public async Task<UploadedImage> UploadImageAsync(Stream fileStream, string fileName, string folder)
    {
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, fileStream),
            Folder = folder,                  // e.g. "products", "blog", "categories"
            Transformation = new Transformation().Quality("auto").FetchFormat("auto"),
            UseFilename = true,
            UniqueFilename = true,
            Overwrite = false
        };

        var result = await _cloudinary.UploadAsync(uploadParams);

        if (result.Error != null)
            throw new InvalidOperationException($"Image upload failed: {result.Error.Message}");

        return new UploadedImage(result.SecureUrl.ToString(), result.PublicId, result.Width, result.Height);
    }

    public async Task DeleteImageAsync(string publicId)
    {
        var result = await _cloudinary.DestroyAsync(new DeletionParams(publicId));
        if (result.Error != null)
            throw new InvalidOperationException($"Image delete failed: {result.Error.Message}");
    }
}
