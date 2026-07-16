namespace HR.Modules.Documents.Features.ListSharedCompanyDocumentsDueForReview;

internal sealed record ListSharedCompanyDocumentsDueForReviewResponse(IReadOnlyList<SharedCompanyDocumentDueForReviewItem> Items);

internal sealed record SharedCompanyDocumentDueForReviewItem(
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
