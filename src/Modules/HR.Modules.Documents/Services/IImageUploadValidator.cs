using HR.SharedKernel;

namespace HR.Modules.Documents.Services;

internal interface IImageUploadValidator
{
    Result Validate(string fileName, string contentType, long fileSize);

    // Reads the stream to confirm the content matches the declared content type and that the
    // parsed pixel dimensions fall within the configured bounds.
    // The caller must reset the stream position after this call.
    Result ValidateImageContent(Stream content, string contentType);
}
