using Microsoft.AspNetCore.Http;

namespace HR.Modules.DataImport.Features.UploadImportFile;

internal sealed class UploadImportFileRequest
{
    public Guid CompanyId { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public IFormFile File { get; init; } = null!;
}
