namespace HR.Modules.Documents.Features.ListSharedCompanyDocuments;

internal sealed record ListSharedCompanyDocumentsResponse(IReadOnlyList<SharedCompanyDocumentListItem> Items);

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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedByName);
