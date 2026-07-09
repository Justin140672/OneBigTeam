namespace HR.Modules.DataImport.Features.GetImportPreview;

internal sealed class GetImportPreviewRequest
{
    public Guid CompanyId { get; init; }
    public Guid ImportSessionId { get; init; }
}
