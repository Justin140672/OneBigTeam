namespace HR.Modules.DataImport.Features.GetImportSessionColumns;

internal sealed class GetImportSessionColumnsRequest
{
    public Guid CompanyId { get; init; }
    public Guid ImportSessionId { get; init; }
}
