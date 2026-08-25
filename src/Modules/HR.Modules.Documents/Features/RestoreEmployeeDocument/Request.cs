namespace HR.Modules.Documents.Features.RestoreEmployeeDocument;

internal sealed record RestoreEmployeeDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid EmployeeDocumentId { get; init; }
}
