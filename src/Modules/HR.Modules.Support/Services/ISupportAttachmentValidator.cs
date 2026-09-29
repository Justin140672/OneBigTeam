using HR.SharedKernel;

namespace HR.Modules.Support.Services;

internal interface ISupportAttachmentValidator
{
    Result ValidateAggregate(int fileCount, long totalSizeBytes);

    Result ValidateFile(string fileName, string contentType, long fileSizeBytes);

    Result ValidateContentSignature(Stream content, string contentType);
}
