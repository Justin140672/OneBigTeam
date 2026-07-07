using HR.SharedKernel;

namespace HR.Modules.DataImport.Services;

internal interface IImportFileValidator
{
    Result Validate(string fileName, string contentType, long fileSize);

    // Reads the first few bytes of the stream to confirm the content matches the declared content type.
    // The caller must reset the stream position after this call.
    Result ValidateContent(Stream content, string contentType);
}
