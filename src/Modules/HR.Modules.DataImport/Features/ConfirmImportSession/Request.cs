namespace HR.Modules.DataImport.Features.ConfirmImportSession;

internal sealed class ConfirmImportSessionRequest
{
    public Guid CompanyId { get; init; }
    public Guid ImportSessionId { get; init; }
}
