namespace HR.Modules.Documents.Features.ListDocumentRequests;

internal sealed record ListDocumentRequestsRequest
{
    public Guid CompanyId  { get; init; }
    public Guid EmployeeId { get; init; }
}
