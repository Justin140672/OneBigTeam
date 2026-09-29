using HR.SharedKernel;

namespace HR.Modules.DataImport.Services;

internal interface IImportFileValidator
{
    Result Validate(string fileName, string contentType, long fileSize);

    Result ValidateContent(Stream content, string contentType);
}
