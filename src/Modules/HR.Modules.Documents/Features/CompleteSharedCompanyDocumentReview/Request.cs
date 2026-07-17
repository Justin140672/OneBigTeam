namespace HR.Modules.Documents.Features.CompleteSharedCompanyDocumentReview;

internal sealed record CompleteSharedCompanyDocumentReviewRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
    public string ReviewNotes { get; init; } = string.Empty;
}
