namespace HR.Modules.Documents.Features.ListSharedCompanyDocuments;

/// <summary>
/// Quick-filter buckets for the Company Documents grid, distinct from the raw document
/// <see cref="Domain.SharedCompanyDocumentStatus"/> filter — these are derived at query time from
/// ReviewDate/Status rather than being a persisted value. Mirrors the Overdue/due-within-7-days
/// bucketing already used by ListSharedCompanyDocumentsDueForReviewHandler for the dashboard widget.
/// </summary>
internal enum SharedCompanyDocumentReviewStatusFilter
{
    /// <summary>ReviewDate falls within the next 7 days (inclusive), on a non-archived, non-expired document.</summary>
    DueSoon = 0,

    /// <summary>ReviewDate is in the past, on a non-archived, non-expired document.</summary>
    Overdue = 1,

    /// <summary>No review has ever been scheduled (ReviewDate is null), on a non-archived, non-expired document.</summary>
    NoReview = 2,

    /// <summary>Document Status is Expired, regardless of ReviewDate.</summary>
    Expired = 3,
}
