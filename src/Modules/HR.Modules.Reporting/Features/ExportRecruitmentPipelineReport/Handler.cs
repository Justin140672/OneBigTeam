using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportRecruitmentPipelineReport;

internal sealed class ExportRecruitmentPipelineReportHandler(
    GetRecruitmentPipelineReportHandler getHandler,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "recruitment-pipeline-report";

    private static readonly string[] ColumnHeaders =
    [
        "Group", "Vacancies", "Applicants", "Interviews", "Offers", "Hires",
    ];

    public async Task<Result<ExportRecruitmentPipelineReportResponse>> HandleAsync(
        ExportRecruitmentPipelineReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var getResult = await getHandler.HandleAsync(
                new GetRecruitmentPipelineReportRequest(request.CompanyId, request.StartDate, request.EndDate, request.GroupBy),
                cancellationToken);

            if (getResult.IsFailure)
            {
                await auditor.PublishFailureAsync(
                    request.CompanyId, ReportId, request.Format.ToString(),
                    managerScopeApplied: false, request, getResult.Error.Message, cancellationToken);
                return Result.Failure<ExportRecruitmentPipelineReportResponse>(getResult.Error);
            }

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

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), getResult.Value!.Items.Count,
                managerScopeApplied: false, request, cancellationToken);

            return Result.Success(new ExportRecruitmentPipelineReportResponse(file));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied: false, request, ex.Message, cancellationToken);
            return Result.Failure<ExportRecruitmentPipelineReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
