namespace HR.Modules.Support.Features.GetSupportDashboard;

internal sealed record GetSupportDashboardResponse(
    int OpenRequestsCount,
    double? AverageStaffResponseTimeHours,
    List<GetSupportDashboardTitleCountDto> TopRequestedFeatures,
    List<GetSupportDashboardTitleCountDto> TopReportedProblems,
    List<GetSupportDashboardTypeBreakdownDto> RequestsByType);

internal sealed record GetSupportDashboardTitleCountDto(string Title, int Count);

internal sealed record GetSupportDashboardTypeBreakdownDto(string Type, int Count);
