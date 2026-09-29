namespace HR.Modules.Documents.Domain;

internal sealed class SharedCompanyDocumentReviewHistory
{
    private SharedCompanyDocumentReviewHistory() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SharedCompanyDocumentId { get; private set; }
    public DateOnly ReviewDate { get; private set; }
    public Guid ReviewedByEmployeeId { get; private set; }
    public string? ReviewNotes { get; private set; }

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
