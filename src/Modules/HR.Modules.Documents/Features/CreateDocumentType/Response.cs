namespace HR.Modules.Documents.Features.CreateDocumentType;

internal sealed record CreateDocumentTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    bool AllowEmployeeUpload,
    DateTimeOffset CreatedAt);
