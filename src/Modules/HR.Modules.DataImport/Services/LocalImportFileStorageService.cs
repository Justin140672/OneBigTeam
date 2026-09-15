namespace HR.Modules.DataImport.Services;

/// <summary>
/// Development implementation that stores files on the local file system.
/// Replace with a cloud implementation (Azure Blob, S3, Supabase Storage, etc.) for production.
/// </summary>
internal sealed class LocalImportFileStorageService : IImportFileStorageService
{
    private readonly string _basePath =
        Path.Combine(Path.GetTempPath(), "onebigteam", "data-import");

    public async Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken)
    {
        // The original file name is untrusted and is recorded separately as display metadata
        // (ImportSession.FileName); the physical storage key never incorporates it, so it cannot
        // be used to escape the storage root via ".." or rooted path segments.
        var extension  = Path.GetExtension(fileName);
        var safeFolder = string.Join('/', storageFolder.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Uri.EscapeDataString(segment)));
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
        var fullPath = ToFullPath(storageKey);
        return Task.FromResult(new Uri(fullPath));
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

    public Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(storageKey);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    // Defence in depth: even though storage keys are now generated server-side (never derived
    // from user-supplied file names), every operation still resolves through this canonical
    // containment check so a malformed or pre-existing unsafe storage key (e.g. from data
    // predating this fix) can never resolve outside the storage root.
    //
    // The containment comparison is deliberately Ordinal (case-sensitive), not
    // OrdinalIgnoreCase: on a case-sensitive filesystem (Linux) a case-insensitive prefix check
    // would treat "../DATA-IMPORT/x" as contained under ".../data-import/" even though it
    // resolves into a different, sibling directory. Ordinal comparison rejects that key outright
    // instead of silently granting access to it. Generated keys are always lowercase hex, so this
    // never rejects a legitimately generated key on either OS.
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
            throw new InvalidOperationException("Resolved storage path escapes the import storage root.");

        return candidate;
    }
}
