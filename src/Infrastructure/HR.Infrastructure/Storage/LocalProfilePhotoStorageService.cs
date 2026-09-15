using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Infrastructure.Storage;

/// <summary>
/// Development implementation that stores profile photos on the local file system.
/// Replace with a cloud implementation (Azure Blob, S3, etc.) for production.
/// </summary>
internal sealed class LocalProfilePhotoStorageService(
    IHttpContextAccessor httpContextAccessor,
    IServiceProvider serviceProvider)
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

    // A raw file:// path here is not loadable by a browser <img> tag (or a redirect-based
    // download endpoint) once served from an http(s):// page — route through the dev-only
    // streaming endpoint in Program.cs instead, which serves the same local file over HTTP.
    //
    // Called from two very different contexts: (1) inline during a request, where
    // IHttpContextAccessor.HttpContext gives us the real scheme/host the browser is using, and
    // (2) from ScanUploadedFileJob, a Hangfire background job with no HttpContext at all — there
    // the previous "http://localhost" fallback pointed at port 80, which nothing listens on (the
    // app binds to whatever dynamic port Aspire/Kestrel assigned), so the job's own
    // HttpClient.GetStreamAsync(downloadUrl) call always failed and the scan never completed
    // inline, only after Hangfire's automatic-retry backoff (30s+) — long enough to blow past
    // every UI poll window waiting on the scan (e.g. MyProfilePhotoHeader.PollForPendingPhotoAsync
    // and EmployeeProfilePhotoHeader.PollForCurrentPhotoAsync). Fall back to the server's own
    // bound address (IServerAddressesFeature) instead, which is available in both contexts.
    public Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var request = httpContextAccessor.HttpContext?.Request;
        var baseUrl = request is not null
            ? $"{request.Scheme}://{request.Host}"
            : GetServerBaseUrl();

        var encodedKey = string.Join('/', storageKey.Split('/').Select(Uri.EscapeDataString));
        return Task.FromResult(new Uri($"{baseUrl}/api/dev/local-storage/profile-photos/{encodedKey}"));
    }

    // Resolved lazily via IServiceProvider (rather than taking IServer as a constructor
    // dependency) so this service doesn't require a real Kestrel host to be present in the DI
    // graph — IServer is only ever registered by WebApplicationBuilder, so a plain
    // ServiceCollection composition check (see ServiceContainerCompositionTests) would otherwise
    // fail container validation even though the real app always has one.
    private string GetServerBaseUrl()
    {
        var addresses = serviceProvider.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault();
        return address ?? "http://localhost";
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
