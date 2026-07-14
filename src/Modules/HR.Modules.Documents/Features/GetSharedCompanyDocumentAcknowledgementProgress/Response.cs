namespace HR.Modules.Documents.Features.GetSharedCompanyDocumentAcknowledgementProgress;

internal sealed record GetSharedCompanyDocumentAcknowledgementProgressResponse(
    Guid DocumentId,
    string DocumentTitle,
    int TotalAssigned,
    int AcknowledgedCount,
    int OutstandingCount,
    int OverdueCount,
    decimal AcknowledgementPercentage,
    IReadOnlyList<SharedCompanyDocumentAcknowledgementProgressItem> Items);

internal sealed record SharedCompanyDocumentAcknowledgementProgressItem(
    Guid EmployeeId,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    string Status,
    DateOnly? DueDate,
    DateTimeOffset? AcknowledgedAt);
