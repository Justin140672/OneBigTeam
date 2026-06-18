using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Features.ListEmployeeDocuments;

internal sealed record ListEmployeeDocumentsRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public DocumentStatus? Status { get; init; }
}
