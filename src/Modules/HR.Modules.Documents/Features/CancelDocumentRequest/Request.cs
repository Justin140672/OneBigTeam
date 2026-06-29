namespace HR.Modules.Documents.Features.CancelDocumentRequest;

internal sealed class CancelDocumentRequestRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid DocumentRequestId { get; init; }
}
