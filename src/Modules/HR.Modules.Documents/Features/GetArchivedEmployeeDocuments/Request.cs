namespace HR.Modules.Documents.Features.GetArchivedEmployeeDocuments;

internal sealed record GetArchivedEmployeeDocumentsRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
