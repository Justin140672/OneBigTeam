using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.GetEmployeeStarterReport;

internal sealed record GetEmployeeStarterReportResponse(
    IReadOnlyList<EmployeeStarterReportItem> Items,
    int TotalCount,
    int Page,
    int PageSize);
