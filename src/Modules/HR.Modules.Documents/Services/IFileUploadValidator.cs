using HR.SharedKernel;

namespace HR.Modules.Documents.Services;

internal interface IFileUploadValidator
{
    Result Validate(string fileName, string contentType, long fileSize);
}
