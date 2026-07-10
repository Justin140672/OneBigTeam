namespace HR.Modules.DataImport.Features.ExportImportErrors;

internal sealed class ExportImportErrorsRequest
{
    public Guid CompanyId { get; init; }
    public Guid ImportSessionId { get; init; }
}
