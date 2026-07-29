namespace HR.Modules.Reporting.Features.SaveReportView;

internal sealed record SaveReportViewResponse(
    Guid Id,
    string ReportId,
    string Name,
    string FilterCriteriaJson,
    bool IsDefault,
    DateTimeOffset CreatedAt);
