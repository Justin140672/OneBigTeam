using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Infrastructure.Storage;

internal sealed class LocalProfilePhotoStorageService(
    IHttpContextAccessor httpContextAccessor,
    IServiceProvider serviceProvider,
    ILocalStorageUrlSigner urlSigner)
    : IProfilePhotoStorageService, ILocalStorageFileReader
{
    private readonly string _basePath = LocalStorageBuckets.GetRootPath(LocalStorageBuckets.ProfilePhotos);

    public async Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken)
    {
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
            : GetServerBaseUrl();

        return Task.FromResult(urlSigner.CreateSignedUrl(baseUrl, LocalStorageBuckets.ProfilePhotos, storageKey));
    }

    public Task<Stream?> OpenLocalReadStreamAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(storageKey);
        return Task.FromResult<Stream?>(File.Exists(fullPath) ? File.OpenRead(fullPath) : null);
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        var fullPath = ToFullPath(storageKey);
        return File.Exists(fullPath)
            ? File.OpenRead(fullPath)
            : throw new FileNotFoundException("The stored profile photo was not found.");
    }

    public Task<string> PromoteToCleanAsync(string quarantineStorageKey, CancellationToken cancellationToken)
    {
        if (!ProfilePhotoStorageKeys.IsQuarantine(quarantineStorageKey))
        {
            return Task.FromResult(quarantineStorageKey);
        }

        var cleanKey = ProfilePhotoStorageKeys.ToCleanKey(quarantineStorageKey);
        var destination = ToFullPath(cleanKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(ToFullPath(quarantineStorageKey), destination, overwrite: false);

        return Task.FromResult(cleanKey);
    }

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
