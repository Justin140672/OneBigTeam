using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetEmployeeStarterReport;

internal sealed class GetEmployeeStarterReportHandler(IEmployeeStarterReader employeeStarterReader)
{
    public async Task<Result<GetEmployeeStarterReportResponse>> HandleAsync(
        GetEmployeeStarterReportRequest request,
        CancellationToken cancellationToken)
    {
        var filter = new ReportFilterCriteria(
            DepartmentId: request.DepartmentId,
            LocationId: request.LocationId,
            PositionProfileId: request.PositionProfileId,
            EmploymentTypeId: request.EmploymentTypeId,
            DateRangeStart: request.DateRangeStart,
            DateRangeEnd: request.DateRangeEnd);

        var pagination = new Pagination(request.Page, request.PageSize);

        var result = await employeeStarterReader.GetEmployeeStartersAsync(
            request.CompanyId,
            filter,
            pagination,
            request.SortBy,
            request.SortDescending,
            cancellationToken);

        return Result.Success(new GetEmployeeStarterReportResponse(
            result.Items,
            result.TotalCount,
            result.PageNumber,
            result.PageSize));
    }
}
