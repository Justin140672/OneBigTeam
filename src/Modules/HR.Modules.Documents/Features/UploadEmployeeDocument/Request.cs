using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadEmployeeDocument;

internal sealed class UploadEmployeeDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid DocumentTypeId { get; init; }
    public DateOnly? IssueDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public IFormFile File { get; init; } = null!;
}
