using HR.SharedKernel;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Services;

internal sealed class FileUploadValidator : IFileUploadValidator
{
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
}
