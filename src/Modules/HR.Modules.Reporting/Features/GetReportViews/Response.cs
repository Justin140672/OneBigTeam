namespace HR.Modules.Reporting.Features.GetReportViews;

internal sealed record SavedReportViewDto(
    Guid Id,
    string ReportId,
    string Name,
    string FilterCriteriaJson,
    bool IsDefault,
    DateTimeOffset CreatedAt);

internal sealed record GetReportViewsResponse(IReadOnlyList<SavedReportViewDto> Views);
