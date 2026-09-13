using MoizPos.Domain.Errors;

namespace MoizPos.Infrastructure.Storage;

public sealed class ImageStorageOptions
{
    /// <summary>Directory, relative to the content root, holding product images.</summary>
    public string ProductImageRoot { get; init; } = "content/products";

    /// <summary>2 MB. Thousands of catalogue images must not bloat the daily backup (R7).</summary>
    public long MaxImageBytes { get; init; } = 2 * 1024 * 1024;
}

public interface IImageStorageService
{
    /// <returns>The relative path to store in products.image_path.</returns>
    Task<string> SaveProductImageAsync(
        Stream content,
        string contentType,
        long lengthBytes,
        CancellationToken cancellationToken = default);

    void DeleteProductImage(string relativePath);
}

/// <summary>
/// Stores product images on disk and keeps only the path in the database (research.md R7).
///
/// BLOBs would bloat every <c>SELECT</c> and, more importantly, the nightly mysqldump the shop
/// depends on for recovery.
/// </summary>
public sealed class ImageStorageService : IImageStorageService
{
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    private readonly ImageStorageOptions _options;
    private readonly string _contentRoot;

    public ImageStorageService(ImageStorageOptions options, string contentRoot)
    {
        _options = options;
        _contentRoot = contentRoot;
    }

    public async Task<string> SaveProductImageAsync(
        Stream content,
        string contentType,
        long lengthBytes,
        CancellationToken cancellationToken = default)
    {
        if (lengthBytes <= 0)
        {
            throw new BusinessRuleViolationException("The uploaded image is empty.");
        }

        if (lengthBytes > _options.MaxImageBytes)
        {
            var limitMb = _options.MaxImageBytes / (1024d * 1024d);

            throw new BusinessRuleViolationException(
                $"Image is too large. The limit is {limitMb:0.#} MB.");
        }

        if (!AllowedTypes.TryGetValue(contentType, out var extension))
        {
            throw new BusinessRuleViolationException(
                "Only JPEG, PNG and WebP images are accepted.");
        }

        var directory = Path.Combine(_contentRoot, _options.ProductImageRoot);
        Directory.CreateDirectory(directory);

        // A generated name, never the client's: an uploaded filename could contain path
        // separators and escape the image directory.
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(directory, fileName);

        await using (var target = File.Create(fullPath))
        {
            await content.CopyToAsync(target, cancellationToken);
        }

        // Forward slashes: this path is served over HTTP, not read from disk by the client.
        return $"{_options.ProductImageRoot}/{fileName}".Replace('\\', '/');
    }

    public void DeleteProductImage(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_contentRoot, normalized));
        var allowedRoot = Path.GetFullPath(Path.Combine(_contentRoot, _options.ProductImageRoot));

        // Refuses to delete anything outside the image directory, whatever the stored path says.
        if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
}
