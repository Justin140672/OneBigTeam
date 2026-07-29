using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.GetEmployeeLeaverReport;

internal sealed record GetEmployeeLeaverReportResponse(
    IReadOnlyList<EmployeeLeaverReportItem> Items,
    int TotalCount,
    int Page,
    int PageSize);
