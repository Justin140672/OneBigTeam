namespace HR.Modules.Documents.Features.DownloadEmployeeDocument;

internal sealed record DownloadEmployeeDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid EmployeeDocumentId { get; init; }
}
