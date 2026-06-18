namespace HR.Modules.Documents.Features.DeleteEmployeeDocument;

internal sealed record DeleteEmployeeDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid EmployeeDocumentId { get; init; }
}
