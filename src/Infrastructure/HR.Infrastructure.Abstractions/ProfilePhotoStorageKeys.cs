namespace HR.Infrastructure.Abstractions;

public static class ProfilePhotoStorageKeys
{
    public const string QuarantinePrefix = "quarantine";
    public const string CleanPrefix = "clean";

    public static string QuarantineFolder(string folder) => $"{QuarantinePrefix}/{folder.Trim('/')}";

    public static bool IsQuarantine(string storageKey) =>
        storageKey.StartsWith(QuarantinePrefix + "/", StringComparison.Ordinal);

    public static string ToCleanKey(string quarantineKey)
    {
        if (!IsQuarantine(quarantineKey))
        {
            return quarantineKey;
        }

        var remainder = quarantineKey[(QuarantinePrefix.Length + 1)..];
        var lastSlash = remainder.LastIndexOf('/');
        var folder = lastSlash < 0 ? string.Empty : remainder[..lastSlash];
        var fileName = lastSlash < 0 ? remainder : remainder[(lastSlash + 1)..];
        var prefix = folder.Length == 0 ? CleanPrefix : $"{CleanPrefix}/{folder}";

        return $"{prefix}/{Guid.NewGuid():N}/{fileName}";
    }
}
