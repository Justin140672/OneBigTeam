using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetEmployeeDirectoryReport;

internal sealed class GetEmployeeDirectoryReportHandler(IEmployeeDirectoryReader employeeDirectoryReader)
{
    public async Task<Result<GetEmployeeDirectoryReportResponse>> HandleAsync(
        GetEmployeeDirectoryReportRequest request,
        CancellationToken cancellationToken)
    {
        var filter = new ReportFilterCriteria(
            DepartmentId: request.DepartmentId,
            LocationId: request.LocationId,
            PositionProfileId: request.PositionProfileId,
            ManagerId: request.ManagerId,
            EmploymentTypeId: request.EmploymentTypeId,
            DateRangeStart: request.DateRangeStart,
            DateRangeEnd: request.DateRangeEnd,
            EmployeeStatus: request.EmployeeStatus);

        var pagination = new Pagination(request.Page, request.PageSize);

        var result = await employeeDirectoryReader.GetEmployeeDirectoryAsync(
            request.CompanyId,
            filter,
            pagination,
            request.SortBy,
            request.SortDescending,
            cancellationToken);

        return Result.Success(new GetEmployeeDirectoryReportResponse(
            result.Items,
            result.TotalCount,
            result.PageNumber,
            result.PageSize));
    }
}
