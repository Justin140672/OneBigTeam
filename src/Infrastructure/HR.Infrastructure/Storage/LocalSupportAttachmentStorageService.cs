using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Http;

namespace HR.Infrastructure.Storage;

/// <summary>
/// Development implementation that stores support request/response attachments on the local file
/// system. Replace with a cloud implementation (Supabase Storage) for production — see
/// <see cref="SupabaseSupportAttachmentStorageService"/>.
/// </summary>
internal sealed class LocalSupportAttachmentStorageService(IHttpContextAccessor httpContextAccessor)
    : ISupportAttachmentStorageService
{
    private readonly string _basePath =
        Path.Combine(Path.GetTempPath(), "onebigteam", "support-attachments");

    public async Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken)
    {
        var storageKey = $"{storageFolder.Trim('/')}/{Guid.NewGuid():N}/{fileName}";
        var fullPath   = ToFullPath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, cancellationToken);

        return storageKey;
    }

    public Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var request = httpContextAccessor.HttpContext?.Request;
        var baseUrl = request is not null
            ? $"{request.Scheme}://{request.Host}"
            : "http://localhost";

        var encodedKey = string.Join('/', storageKey.Split('/').Select(Uri.EscapeDataString));
        return Task.FromResult(new Uri($"{baseUrl}/api/dev/local-storage/support-attachments/{encodedKey}"));
    }

    public Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(storageKey);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    private string ToFullPath(string storageKey) =>
        Path.Combine(_basePath, storageKey.Replace('/', Path.DirectorySeparatorChar));
}
