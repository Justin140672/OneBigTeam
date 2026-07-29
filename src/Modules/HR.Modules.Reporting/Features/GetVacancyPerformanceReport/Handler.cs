using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetVacancyPerformanceReport;

internal sealed class GetVacancyPerformanceReportHandler(IVacancyPerformanceReader vacancyPerformanceReader)
{
    public async Task<Result<GetVacancyPerformanceReportResponse>> HandleAsync(
        GetVacancyPerformanceReportRequest request,
        CancellationToken cancellationToken)
    {
        var items = await vacancyPerformanceReader.GetVacancyPerformanceAsync(
            request.CompanyId, request.StartDate, request.EndDate, cancellationToken);

        var rows = items
            .Select(i => new VacancyPerformanceReportRow(
                i.VacancyId, i.VacancyTitle, i.DaysOpen, i.ApplicantCount, i.InterviewCount, i.OfferCount, i.HireDate))
            .ToList();

        return Result.Success(new GetVacancyPerformanceReportResponse(rows));
    }
}
