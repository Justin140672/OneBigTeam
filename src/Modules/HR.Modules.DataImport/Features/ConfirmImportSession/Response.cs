namespace HR.Modules.DataImport.Features.ConfirmImportSession;

internal sealed record ConfirmImportSessionResponse(
    Guid ImportSessionId,
    string Status,
    int CreatedCount,
    int FailedCount);
