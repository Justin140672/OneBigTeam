using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportRecruitmentPipelineReport;

internal sealed class ExportRecruitmentPipelineReportHandler(
    GetRecruitmentPipelineReportHandler getHandler,
    IReportExporter reportExporter)
{
    private static readonly string[] ColumnHeaders =
    [
        "Group", "Vacancies", "Applicants", "Interviews", "Offers", "Hires",
    ];

    public async Task<Result<ExportRecruitmentPipelineReportResponse>> HandleAsync(
        ExportRecruitmentPipelineReportRequest request,
        CancellationToken cancellationToken)
    {
        var getResult = await getHandler.HandleAsync(
            new GetRecruitmentPipelineReportRequest(request.CompanyId, request.StartDate, request.EndDate, request.GroupBy),
            cancellationToken);

        if (getResult.IsFailure)
            return Result.Failure<ExportRecruitmentPipelineReportResponse>(getResult.Error);

        var rows = getResult.Value!.Items
            .Select(item => (IReadOnlyList<string?>)new List<string?>
            {
                item.GroupLabel,
                item.Vacancies.ToString(),
                item.Applicants.ToString(),
                item.Interviews.ToString(),
                item.Offers.ToString(),
                item.Hires.ToString(),
            })
            .ToList();

        var exportData = new ReportExportData("Recruitment Pipeline Report", ColumnHeaders, rows);
        var file = reportExporter.Export(request.Format, exportData);

        return Result.Success(new ExportRecruitmentPipelineReportResponse(file));
    }
}
