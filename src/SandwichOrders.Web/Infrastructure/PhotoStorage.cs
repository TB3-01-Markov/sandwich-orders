using System.Text.RegularExpressions;

namespace SandwichOrders.Web.Infrastructure;

public enum PhotoError
{
    None,
    TooLarge,
    WrongType
}

public record PhotoSaveResult(string? Path, PhotoError Error);

// Stores sandwich photos on disk. The folder comes from configuration (Uploads:Path), so Docker can put it on a volume.
// A file is accepted only if its first bytes really are a JPG, PNG or WebP image; the file name the browser sends is ignored.
public partial class PhotoStorage(IConfiguration configuration, IWebHostEnvironment environment)
{
    public const long MaxBytes = 2 * 1024 * 1024;
    public const string UrlPrefix = "/uploads/";

    public string Directory { get; } =
        configuration["Uploads:Path"] ?? System.IO.Path.Combine(environment.ContentRootPath, "uploads");

    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png|webp)$")]
    private static partial Regex StoredNamePattern();

    public async Task<PhotoSaveResult> SaveAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file.Length > MaxBytes)
        {
            return new PhotoSaveResult(null, PhotoError.TooLarge);
        }

        var header = new byte[12];
        int read;
        await using (var stream = file.OpenReadStream())
        {
            read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        }

        var extension = DetectExtension(header.AsSpan(0, read));
        if (extension is null)
        {
            return new PhotoSaveResult(null, PhotoError.WrongType);
        }

        System.IO.Directory.CreateDirectory(Directory);
        var name = Guid.NewGuid().ToString("N") + extension;
        await using (var target = File.Create(System.IO.Path.Combine(Directory, name)))
        {
            await file.CopyToAsync(target, ct);
        }

        return new PhotoSaveResult(UrlPrefix + name, PhotoError.None);
    }

    // Only files this class created can be deleted: the name must match exactly, so no path can escape the folder.
    public void Delete(string? imagePath)
    {
        if (imagePath is null || !imagePath.StartsWith(UrlPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var name = imagePath[UrlPrefix.Length..];
        if (!StoredNamePattern().IsMatch(name))
        {
            return;
        }

        try
        {
            File.Delete(System.IO.Path.Combine(Directory, name));
        }
        catch (IOException)
        {
            // A leftover file is harmless.
        }
    }

    public static string? DetectExtension(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF)
        {
            return ".jpg";
        }

        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47
            && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A)
        {
            return ".png";
        }

        // WebP: "RIFF" <size> "WEBP"
        if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F'
            && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P')
        {
            return ".webp";
        }

        return null;
    }
}
