using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetVacancyPerformanceReport;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportVacancyPerformanceReport;

internal sealed class ExportVacancyPerformanceReportHandler(
    GetVacancyPerformanceReportHandler getHandler,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "vacancy-performance-report";

    private static readonly string[] ColumnHeaders =
    [
        "Vacancy", "Days Open", "Applicants", "Interviews", "Offers", "Hire Date",
    ];

    public async Task<Result<ExportVacancyPerformanceReportResponse>> HandleAsync(
        ExportVacancyPerformanceReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var getResult = await getHandler.HandleAsync(
                new GetVacancyPerformanceReportRequest(request.CompanyId, request.StartDate, request.EndDate),
                cancellationToken);

            if (getResult.IsFailure)
            {
                await auditor.PublishFailureAsync(
                    request.CompanyId, ReportId, request.Format.ToString(),
                    managerScopeApplied: false, request, getResult.Error.Message, cancellationToken);
                return Result.Failure<ExportVacancyPerformanceReportResponse>(getResult.Error);
            }

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

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), getResult.Value!.Items.Count,
                managerScopeApplied: false, request, cancellationToken);

            return Result.Success(new ExportVacancyPerformanceReportResponse(file));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied: false, request, ex.Message, cancellationToken);
            return Result.Failure<ExportVacancyPerformanceReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
