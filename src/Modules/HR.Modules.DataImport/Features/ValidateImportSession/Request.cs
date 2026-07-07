namespace HR.Modules.DataImport.Features.ValidateImportSession;

internal sealed class ValidateImportSessionRequest
{
    public Guid CompanyId { get; init; }
    public Guid ImportSessionId { get; init; }
}
