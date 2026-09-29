using HR.SharedKernel;

namespace HR.Modules.Documents.Services;

internal interface IImageUploadValidator
{
    Result Validate(string fileName, string contentType, long fileSize);

    Result ValidateImageContent(Stream content, string contentType);
}
