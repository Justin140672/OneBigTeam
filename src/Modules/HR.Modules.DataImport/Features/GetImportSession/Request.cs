namespace HR.Modules.DataImport.Features.GetImportSession;

internal sealed class GetImportSessionRequest
{
    public Guid CompanyId { get; init; }
    public Guid ImportSessionId { get; init; }
}
