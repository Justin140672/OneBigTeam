namespace HR.Modules.Documents.Features.ListSharedCompanyDocuments;

internal sealed record ListSharedCompanyDocumentsResponse(
    IReadOnlyList<SharedCompanyDocumentListItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

internal sealed record SharedCompanyDocumentListItem(
    Guid Id,
    string Title,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    string FileName,
    int VersionNumber,
    string Status,
    DateOnly? EffectiveDate,
    DateOnly? ReviewDate,
    string ReviewFrequency,
    Guid? ReviewOwnerEmployeeId,
    string? ReviewOwnerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedByName);
