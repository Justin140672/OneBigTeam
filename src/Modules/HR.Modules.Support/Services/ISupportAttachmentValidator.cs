using HR.SharedKernel;

namespace HR.Modules.Support.Services;

internal interface ISupportAttachmentValidator
{
    /// <summary>Whole-request check, run before any file is uploaded (fail fast, upload nothing).</summary>
    Result ValidateAggregate(int fileCount, long totalSizeBytes);

    Result ValidateFile(string fileName, string contentType, long fileSizeBytes);

    /// <summary>Reads (and rewinds) the first few bytes of a seekable stream to confirm the content
    /// matches the declared content type.</summary>
    Result ValidateContentSignature(Stream content, string contentType);
}
