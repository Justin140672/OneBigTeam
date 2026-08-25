using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportRecruitmentPipelineSummaryReport;

internal sealed class ExportRecruitmentPipelineSummaryReportHandler(
    IRecruitmentPipelineSummaryReader recruitmentPipelineSummaryReader,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "recruitment-pipeline-summary";

    private static readonly string[] FixedColumnHeaders =
    [
        "Vacancy", "Position Profile", "Department", "Status", "Date Opened", "Candidates",
    ];

    public async Task<Result<ExportRecruitmentPipelineSummaryReportResponse>> HandleAsync(
        ExportRecruitmentPipelineSummaryReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await recruitmentPipelineSummaryReader.GetSummaryAsync(
                request.CompanyId, request.IncludeClosed, cancellationToken);

            var columnHeaders = FixedColumnHeaders
                .Concat(result.Stages.Select(s => s.StageName))
                .ToList();

            var rows = result.Vacancies
                .Select(v =>
                {
                    var row = new List<string?>
                    {
                        v.VacancyTitle,
                        v.PositionProfileTitle,
                        v.DepartmentName,
                        v.Status,
                        v.OpenedAt?.ToString("yyyy-MM-dd"),
                        v.CandidateCount.ToString(),
                    };

                    row.AddRange(result.Stages.Select(s =>
                        v.CandidatesByStage.GetValueOrDefault(s.StageId, 0).ToString()));

                    return (IReadOnlyList<string?>)row;
                })
                .ToList();

            var exportData = new ReportExportData("Recruitment Pipeline Summary", columnHeaders, rows);
            var file = reportExporter.Export(request.Format, exportData);

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), result.Vacancies.Count,
                managerScopeApplied: false, request, cancellationToken);

            return Result.Success(new ExportRecruitmentPipelineSummaryReportResponse(file));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied: false, request, ex.Message, cancellationToken);
            return Result.Failure<ExportRecruitmentPipelineSummaryReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
