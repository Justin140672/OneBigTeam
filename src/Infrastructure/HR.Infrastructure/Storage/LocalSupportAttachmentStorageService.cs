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
        // The original file name is untrusted; the physical storage key never incorporates it, so
        // it cannot be used to escape the storage root via ".." or rooted path segments.
        var extension  = Path.GetExtension(fileName);
        var safeFolder = string.Join('/', storageFolder.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        var storageKey = $"{safeFolder}/{Guid.NewGuid():N}{extension}";
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

    // Defence in depth: resolves through a canonical containment check so a malformed or
    // pre-existing unsafe storage key can never resolve outside the storage root.
    //
    // The containment comparison is deliberately Ordinal (case-sensitive), not
    // OrdinalIgnoreCase: on a case-sensitive filesystem (Linux) a case-insensitive prefix check
    // would treat a differently-cased sibling directory as contained under the storage root even
    // though it resolves elsewhere. Ordinal comparison rejects that key outright. Generated keys
    // are always lowercase hex, so this never rejects a legitimately generated key on either OS.
    private string ToFullPath(string storageKey)
    {
        var segments = storageKey.Split(['/', '\\']);
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.Contains(':'))
                throw new InvalidOperationException($"Storage key '{storageKey}' contains an invalid path segment.");
        }

        if (Path.IsPathRooted(storageKey))
            throw new InvalidOperationException($"Storage key '{storageKey}' must be relative.");

        var basePathFull = Path.GetFullPath(_basePath);
        var candidate     = Path.GetFullPath(Path.Combine(basePathFull, string.Join(Path.DirectorySeparatorChar, segments)));

        var basePathWithSeparator = basePathFull.EndsWith(Path.DirectorySeparatorChar)
            ? basePathFull
            : basePathFull + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(basePathWithSeparator, StringComparison.Ordinal))
            throw new InvalidOperationException("Resolved storage path escapes the storage root.");

        return candidate;
    }
}
