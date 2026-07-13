using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Http;

namespace HR.Infrastructure.Storage;

/// <summary>
/// Development implementation that stores profile photos on the local file system.
/// Replace with a cloud implementation (Azure Blob, S3, etc.) for production.
/// </summary>
internal sealed class LocalProfilePhotoStorageService(IHttpContextAccessor httpContextAccessor)
    : IProfilePhotoStorageService
{
    private readonly string _basePath =
        Path.Combine(Path.GetTempPath(), "onebigteam", "profile-photos");

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

    // A raw file:// path here is not loadable by a browser <img> tag (or a redirect-based
    // download endpoint) once served from an http(s):// page — route through the dev-only
    // streaming endpoint in Program.cs instead, which serves the same local file over HTTP.
    public Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var request = httpContextAccessor.HttpContext?.Request;
        var baseUrl = request is not null
            ? $"{request.Scheme}://{request.Host}"
            : "http://localhost";

        var encodedKey = string.Join('/', storageKey.Split('/').Select(Uri.EscapeDataString));
        return Task.FromResult(new Uri($"{baseUrl}/api/dev/local-storage/profile-photos/{encodedKey}"));
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
