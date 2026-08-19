using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportRecruitmentPipelineSummaryReport;

internal sealed class ExportRecruitmentPipelineSummaryReportHandler(
    IRecruitmentPipelineSummaryReader recruitmentPipelineSummaryReader,
    IReportExporter reportExporter)
{
    private static readonly string[] FixedColumnHeaders =
    [
        "Vacancy", "Position Profile", "Department", "Status", "Date Opened", "Candidates",
    ];

    public async Task<Result<ExportRecruitmentPipelineSummaryReportResponse>> HandleAsync(
        ExportRecruitmentPipelineSummaryReportRequest request,
        CancellationToken cancellationToken)
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

        return Result.Success(new ExportRecruitmentPipelineSummaryReportResponse(file));
    }
}
