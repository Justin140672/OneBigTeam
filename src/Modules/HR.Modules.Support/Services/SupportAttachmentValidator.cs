using HR.SharedKernel;

namespace HR.Modules.Support.Services;

internal sealed class SupportAttachmentValidator : ISupportAttachmentValidator
{
    public Result ValidateAggregate(int fileCount, long totalSizeBytes)
    {
        if (fileCount > SupportAttachmentPolicy.MaxFileCount)
        {
            return Result.Failure(Error.Validation(
                $"No more than {SupportAttachmentPolicy.MaxFileCount} files may be attached."));
        }

        if (totalSizeBytes > SupportAttachmentPolicy.MaxTotalSizeBytes)
        {
            var maxMb = SupportAttachmentPolicy.MaxTotalSizeBytes / (1024.0 * 1024.0);
            return Result.Failure(Error.Validation(
                $"The combined size of all attachments exceeds the maximum allowed size of {maxMb:0.##} MB."));
        }

        return Result.Success();
    }

    public Result ValidateFile(string fileName, string contentType, long fileSizeBytes)
    {
        if (fileSizeBytes <= 0)
            return Result.Failure(Error.Validation("File must not be empty."));

        if (fileSizeBytes > SupportAttachmentPolicy.MaxFileSizeBytes)
        {
            var maxMb = SupportAttachmentPolicy.MaxFileSizeBytes / (1024.0 * 1024.0);
            return Result.Failure(Error.Validation($"File size exceeds the maximum allowed size of {maxMb:0.##} MB."));
        }

        // Path.GetExtension on a caller-supplied name is safe (pure string parsing, no filesystem
        // access) — this is only ever used to check the extension allow-list; the sanitised name
        // used for storage/display is derived separately (see SubmitSupportRequestHandler).
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension)
            || !SupportAttachmentPolicy.AllowedExtensions.Contains(extension.ToLowerInvariant()))
        {
            var allowed = string.Join(", ", SupportAttachmentPolicy.AllowedExtensions);
            return Result.Failure(Error.Validation($"File type '{extension}' is not allowed. Allowed types: {allowed}."));
        }

        var normalizedContentType = contentType.Split(';')[0].Trim();
        if (!SupportAttachmentPolicy.AllowedContentTypes.Contains(normalizedContentType, StringComparer.OrdinalIgnoreCase))
        {
            var allowed = string.Join(", ", SupportAttachmentPolicy.AllowedContentTypes);
            return Result.Failure(Error.Validation($"Content type '{normalizedContentType}' is not allowed. Allowed types: {allowed}."));
        }

        return Result.Success();
    }

    public Result ValidateContentSignature(Stream content, string contentType)
    {
        var normalizedContentType = contentType.Split(';')[0].Trim();

        if (!SupportAttachmentPolicy.MagicBytes.TryGetValue(normalizedContentType, out var signatures))
            return Result.Success(); // no known signature for this type (e.g. text/plain); defer to other checks

        if (!content.CanSeek)
            return Result.Failure(Error.Validation("File content could not be verified."));

        var originalPosition = content.Position;
        Span<byte> header = stackalloc byte[4];
        var read = content.Read(header);
        content.Position = originalPosition;

        if (read < header.Length)
            return Result.Failure(Error.Validation("File content is too short to be a valid file of the declared type."));

        foreach (var sig in signatures)
        {
            if (header.SequenceEqual(sig))
                return Result.Success();
        }

        return Result.Failure(Error.Validation(
            $"File content does not match the declared type '{normalizedContentType}'. " +
            "Ensure the file has not been renamed or tampered with."));
    }
}
