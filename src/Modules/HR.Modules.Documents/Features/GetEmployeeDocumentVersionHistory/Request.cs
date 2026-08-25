namespace HR.Modules.Documents.Features.GetEmployeeDocumentVersionHistory;

internal sealed record GetEmployeeDocumentVersionHistoryRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid EmployeeDocumentId { get; init; }
}
