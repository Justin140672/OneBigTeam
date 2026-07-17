namespace HR.Modules.Documents.Features.CompleteSharedCompanyDocumentReview;

internal sealed record CompleteSharedCompanyDocumentReviewResponse(
    Guid Id,
    Guid CompanyId,
    DateOnly? ReviewDate,
    DateOnly LastReviewedAt,
    Guid LastReviewedByEmployeeId,
    string? LastReviewNotes);
