using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.GetEmployeeDirectoryReport;

internal sealed record GetEmployeeDirectoryReportResponse(
    IReadOnlyList<EmployeeDirectoryReportItem> Items,
    int TotalCount,
    int Page,
    int PageSize);
