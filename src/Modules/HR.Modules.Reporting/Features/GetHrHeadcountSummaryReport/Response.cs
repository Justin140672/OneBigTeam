using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.GetHrHeadcountSummaryReport;

internal sealed record GetHrHeadcountSummaryReportResponse(
    IReadOnlyList<HrHeadcountSummaryItem> Items,
    int TotalHeadcount,
    int ActiveEmployees,
    int FutureStarters,
    int Leavers,
    decimal TotalFte);
