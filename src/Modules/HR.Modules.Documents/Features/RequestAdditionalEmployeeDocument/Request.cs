namespace HR.Modules.Documents.Features.RequestAdditionalEmployeeDocument;

internal sealed class RequestAdditionalEmployeeDocumentRequest
{
    public Guid CompanyId      { get; init; }
    public Guid EmployeeId     { get; init; }
    public Guid DocumentTypeId { get; init; }
    public DateOnly? DueDate   { get; init; }
}
