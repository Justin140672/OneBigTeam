namespace HR.Modules.Documents.Domain;

/// <summary>
/// A point-in-time record of one completed review of a <see cref="SharedCompanyDocument"/>. A row
/// is written every time a review is completed, so "review history" is always a complete list of
/// every review that has ever been performed on the document, not just the most recent one.
/// </summary>
internal sealed class SharedCompanyDocumentReviewHistory
{
    private SharedCompanyDocumentReviewHistory() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SharedCompanyDocumentId { get; private set; }
    public DateOnly ReviewDate { get; private set; }
    public Guid ReviewedByEmployeeId { get; private set; }
    public string? ReviewNotes { get; private set; }

    // Snapshot of SharedCompanyDocument.ReviewDate (the due date) as it stood immediately before
    // this review overwrote it — like ReviewNotes, this is point-in-time history, not a
    // user-editable field of the review itself.
    public DateOnly? PreviousReviewDate { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static SharedCompanyDocumentReviewHistory Create(
        Guid id,
        Guid companyId,
        Guid sharedCompanyDocumentId,
        DateOnly reviewDate,
        Guid reviewedByEmployeeId,
        string? reviewNotes,
        DateOnly? previousReviewDate,
        DateTimeOffset createdAt) => new()
    {
        Id                      = id,
        CompanyId               = companyId,
        SharedCompanyDocumentId = sharedCompanyDocumentId,
        ReviewDate              = reviewDate,
        ReviewedByEmployeeId    = reviewedByEmployeeId,
        ReviewNotes             = string.IsNullOrWhiteSpace(reviewNotes) ? null : reviewNotes.Trim(),
        PreviousReviewDate      = previousReviewDate,
        CreatedAt               = createdAt,
    };
}
