using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetVacancyPerformanceReport;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportVacancyPerformanceReport;

internal sealed class ExportVacancyPerformanceReportHandler(
    GetVacancyPerformanceReportHandler getHandler,
    IReportExporter reportExporter)
{
    private static readonly string[] ColumnHeaders =
    [
        "Vacancy", "Days Open", "Applicants", "Interviews", "Offers", "Hire Date",
    ];

    public async Task<Result<ExportVacancyPerformanceReportResponse>> HandleAsync(
        ExportVacancyPerformanceReportRequest request,
        CancellationToken cancellationToken)
    {
        var getResult = await getHandler.HandleAsync(
            new GetVacancyPerformanceReportRequest(request.CompanyId, request.StartDate, request.EndDate),
            cancellationToken);

        if (getResult.IsFailure)
            return Result.Failure<ExportVacancyPerformanceReportResponse>(getResult.Error);

        var rows = getResult.Value!.Items
            .Select(item => (IReadOnlyList<string?>)new List<string?>
            {
                item.VacancyTitle,
                item.DaysOpen.ToString(),
                item.ApplicantCount.ToString(),
                item.InterviewCount.ToString(),
                item.OfferCount.ToString(),
                item.HireDate?.ToString("yyyy-MM-dd"),
            })
            .ToList();

        var exportData = new ReportExportData("Vacancy Performance Report", ColumnHeaders, rows);
        var file = reportExporter.Export(request.Format, exportData);

        return Result.Success(new ExportVacancyPerformanceReportResponse(file));
    }
}
