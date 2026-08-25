namespace HR.Modules.Documents.Features.RestoreEmployeeDocument;

internal sealed record RestoreEmployeeDocumentResponse(
    Guid EmployeeDocumentId,
    Guid CompanyId,
    Guid RestoredByUserId,
    DateTimeOffset RestoredAt);
