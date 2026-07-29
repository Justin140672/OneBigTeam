namespace HR.Modules.Reporting.Features.GetProbationReport;

internal sealed record GetProbationReportResponse(
    IReadOnlyList<ProbationReportRow> Items,
    int CurrentProbationCount,
    int DueReviewCount,
    int OverdueReviewCount,
    int PassedCount,
    int ExtendedCount);

internal sealed record ProbationReportRow(
    Guid EmployeeId,
    string EmployeeName,
    string Status,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    int DueReviews,
    int OverdueReviews);
