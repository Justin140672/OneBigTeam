using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Features.SearchEmployeeDocuments;

internal sealed record SearchEmployeeDocumentsRequest
{
    public Guid CompanyId { get; init; }

    public string? SearchText { get; init; }

    public Guid? DocumentTypeId { get; init; }

    public Guid? EmployeeId { get; init; }

    public DocumentStatus? Status { get; init; }

    public Guid? UploadedBy { get; init; }

    public DateOnly? UploadedFrom { get; init; }
    public DateOnly? UploadedTo { get; init; }

    public DateOnly? ExpiresFrom { get; init; }
    public DateOnly? ExpiresTo { get; init; }

    public bool IncludeArchived { get; init; }

    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
