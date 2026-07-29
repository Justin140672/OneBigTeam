using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetEmployeeLeaverReport;

internal sealed class GetEmployeeLeaverReportHandler(IEmployeeLeaverReader employeeLeaverReader)
{
    public async Task<Result<GetEmployeeLeaverReportResponse>> HandleAsync(
        GetEmployeeLeaverReportRequest request,
        CancellationToken cancellationToken)
    {
        var filter = new ReportFilterCriteria(
            DepartmentId: request.DepartmentId,
            PositionProfileId: request.PositionProfileId,
            DateRangeStart: request.DateRangeStart,
            DateRangeEnd: request.DateRangeEnd);

        var pagination = new Pagination(request.Page, request.PageSize);

        var result = await employeeLeaverReader.GetEmployeeLeaversAsync(
            request.CompanyId,
            filter,
            pagination,
            request.SortBy,
            request.SortDescending,
            cancellationToken);

        return Result.Success(new GetEmployeeLeaverReportResponse(
            result.Items,
            result.TotalCount,
            result.PageNumber,
            result.PageSize));
    }
}
