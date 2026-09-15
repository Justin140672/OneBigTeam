using HR.Infrastructure.Abstractions;

namespace HR.Infrastructure.Storage;

/// <summary>
/// Development/test implementation of <see cref="IOrganisationDataExportStorage"/> that stores
/// export ZIP archives on the local file system. Mirrors
/// <see cref="LocalSupportAttachmentStorageService"/>. Replace with
/// <see cref="SupabaseOrganisationDataExportStorage"/> for hosted environments.
/// </summary>
internal sealed class LocalOrganisationDataExportStorage : IOrganisationDataExportStorage
{
    private readonly string _basePath =
        Path.Combine(Path.GetTempPath(), "onebigteam", "organisation-exports");

    public async Task<string> UploadAsync(Guid companyId, Guid exportId, Guid attemptToken, Stream content, CancellationToken cancellationToken)
    {
        var storageKey = $"organisation-exports/{companyId}/{exportId}/{attemptToken}.zip";
        var fullPath = ToFullPath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, cancellationToken);

        return storageKey;
    }

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(storageKey);
        if (!File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);

        return Task.FromResult<Stream?>(File.OpenRead(fullPath));
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(storageKey);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAttemptKeysAsync(Guid companyId, Guid exportId, CancellationToken cancellationToken)
    {
        var prefix = $"organisation-exports/{companyId}/{exportId}";
        var dir = ToFullPath(prefix);
        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var keys = Directory.EnumerateFiles(dir)
            .Select(f => $"{prefix}/{Path.GetFileName(f)}")
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    // Defence in depth: this storage key is built server-side from GUIDs only, but still resolves
    // through a canonical containment check so a malformed key can never resolve outside the root.
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
