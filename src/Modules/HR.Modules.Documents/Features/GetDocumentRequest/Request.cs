namespace HR.Modules.Documents.Features.GetDocumentRequest;

internal sealed record GetDocumentRequestRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid Id { get; init; }
}
