namespace HR.Modules.Reporting.Features.SaveReportView;

internal sealed record SaveReportViewRequest(
    Guid CompanyId,
    string ReportId,
    string Name,
    string FilterCriteriaJson,
    bool? IsDefault);
