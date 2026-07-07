using HR.SharedKernel;
using Microsoft.Extensions.Options;

namespace HR.Modules.DataImport.Services;

internal sealed class ImportFileValidator : IImportFileValidator
{
    // Maps a declared content type to the magic byte sequences that identify it.
    // XLSX is a ZIP/OOXML container, so it shares the PK signatures used for other Office Open XML formats.
    // CSV is plain text with no reliable magic byte, so it has no entry here and falls through to Success.
    private static readonly Dictionary<string, byte[][]> MagicBytes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] =
        [
            [0x50, 0x4B, 0x03, 0x04], // PK (ZIP)
            [0x50, 0x4B, 0x05, 0x06], // PK empty archive
        ],
    };

    private readonly ImportFileUploadOptions _options;

    public ImportFileValidator(IOptions<ImportFileUploadOptions> options)
    {
        _options = options.Value;
    }

    public Result Validate(string fileName, string contentType, long fileSize)
    {
        if (fileSize <= 0)
            return Result.Failure(Error.Validation("File must not be empty."));

        if (fileSize > _options.MaxFileSizeBytes)
        {
            var maxMb = _options.MaxFileSizeBytes / (1024.0 * 1024.0);
            return Result.Failure(Error.Validation($"File size exceeds the maximum allowed size of {maxMb:0.##} MB."));
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) ||
            !_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            var allowed = string.Join(", ", _options.AllowedExtensions);
            return Result.Failure(Error.Validation($"File type '{extension}' is not allowed. Allowed types: {allowed}."));
        }

        var normalizedContentType = contentType.Split(';')[0].Trim();
        if (!_options.AllowedContentTypes.Contains(normalizedContentType, StringComparer.OrdinalIgnoreCase))
        {
            var allowed = string.Join(", ", _options.AllowedContentTypes);
            return Result.Failure(Error.Validation($"Content type '{normalizedContentType}' is not allowed. Allowed types: {allowed}."));
        }

        return Result.Success();
    }

    public Result ValidateContent(Stream content, string contentType)
    {
        var normalizedContentType = contentType.Split(';')[0].Trim();

        if (!MagicBytes.TryGetValue(normalizedContentType, out var signatures))
            return Result.Success(); // no known signature for this type (e.g. CSV); defer to other checks

        Span<byte> header = stackalloc byte[4];
        var read = content.Read(header);

        if (read < header.Length)
            return Result.Failure(Error.Validation("File content is too short to be a valid file."));

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
