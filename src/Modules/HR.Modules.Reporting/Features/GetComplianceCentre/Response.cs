namespace HR.Modules.Reporting.Features.GetComplianceCentre;

/// <summary>
/// The consolidated compliance categories surfaced by the Compliance Centre (ADM-02).
/// </summary>
internal enum ComplianceCategory
{
    ExpiringVisa,
    ExpiringCertification,
    ExpiringOtherDocument,
    MissingRequiredDocument,
    OutstandingDocumentRequest,
    ProbationReview
}

/// <summary>
/// Priority bucket for a single compliance item. Computed centrally from the item's due/expiry
/// date so every category is judged against the same clock.
/// </summary>
internal enum ComplianceSeverity
{
    Overdue,
    DueSoon,
    Informational
}

internal sealed record GetComplianceCentreResponse(
    IReadOnlyList<ComplianceItemRow> Items,
    IReadOnlyList<ComplianceCategorySummary> CategorySummaries,
    ComplianceCentreSummary Summary,
    int TotalCount,
    bool IsTruncated,
    bool NoActionRequired);

internal sealed record ComplianceItemRow(
    Guid EmployeeId,
    string EmployeeName,
    string? Department,
    string Category,
    string CategoryLabel,
    string Detail,
    DateOnly? DueDate,
    string Severity,
    string DeepLinkUrl);

internal sealed record ComplianceCategorySummary(
    string Category,
    string CategoryLabel,
    int Total,
    int Overdue,
    int DueSoon,
    int Informational);

internal sealed record ComplianceCentreSummary(
    int Total,
    int Overdue,
    int DueSoon,
    int Informational);
