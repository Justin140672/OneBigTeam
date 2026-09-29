using HR.SharedKernel;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Services;

internal sealed class FileUploadValidator : IFileUploadValidator
{
    private static readonly Dictionary<string, byte[][]> MagicBytes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] =
        [
            [0x25, 0x50, 0x44, 0x46],
        ],
        ["application/msword"] =
        [
            [0xD0, 0xCF, 0x11, 0xE0],
        ],
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] =
        [
            [0x50, 0x4B, 0x03, 0x04],
            [0x50, 0x4B, 0x05, 0x06],
        ],
        ["image/jpeg"] =
        [
            [0xFF, 0xD8, 0xFF, 0xE0],
            [0xFF, 0xD8, 0xFF, 0xE1],
            [0xFF, 0xD8, 0xFF, 0xE8],
            [0xFF, 0xD8, 0xFF, 0xDB],
        ],
        ["image/png"] =
        [
            [0x89, 0x50, 0x4E, 0x47],
        ],
    };

    private readonly FileUploadOptions _options;

    public FileUploadValidator(IOptions<FileUploadOptions> options)
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
            return Result.Success();

        Span<byte> header = stackalloc byte[4];
        var read = content.Read(header);

        if (read < header.Length)
            return Result.Failure(Error.Validation("File content is too short to be a valid document."));

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
