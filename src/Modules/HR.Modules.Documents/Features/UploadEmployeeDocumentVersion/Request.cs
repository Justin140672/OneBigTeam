using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadEmployeeDocumentVersion;

internal sealed class UploadEmployeeDocumentVersionRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid EmployeeDocumentId { get; init; }
    public DateOnly? IssueDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public IFormFile File { get; init; } = null!;
}
