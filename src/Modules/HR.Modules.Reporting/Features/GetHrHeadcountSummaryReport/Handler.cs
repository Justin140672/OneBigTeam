using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetHrHeadcountSummaryReport;

internal sealed class GetHrHeadcountSummaryReportHandler(IHrHeadcountSummaryReader hrHeadcountSummaryReader)
{
    public async Task<Result<GetHrHeadcountSummaryReportResponse>> HandleAsync(
        GetHrHeadcountSummaryReportRequest request,
        CancellationToken cancellationToken)
    {
        var filter = new ReportFilterCriteria(
            DepartmentId: request.DepartmentId,
            LocationId: request.LocationId,
            EmploymentTypeId: request.EmploymentTypeId,
            EmployeeStatus: request.EmployeeStatus);

        var result = await hrHeadcountSummaryReader.GetHeadcountSummaryAsync(request.CompanyId, filter, cancellationToken);

        return Result.Success(new GetHrHeadcountSummaryReportResponse(
            result.Items,
            result.TotalHeadcount,
            result.ActiveEmployees,
            result.FutureStarters,
            result.Leavers,
            result.TotalFte));
    }
}
