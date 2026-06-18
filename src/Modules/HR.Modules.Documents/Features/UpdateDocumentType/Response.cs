namespace HR.Modules.Documents.Features.UpdateDocumentType;

internal sealed record UpdateDocumentTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset UpdatedAt);
