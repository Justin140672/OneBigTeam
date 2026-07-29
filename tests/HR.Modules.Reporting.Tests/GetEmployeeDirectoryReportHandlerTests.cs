using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetEmployeeDirectoryReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetEmployeeDirectoryReportHandlerTests
{
    private static EmployeeDirectoryReportItem BuildItem(Guid employeeId) =>
        new(
            employeeId,
            "EMP-001",
            "Alice Smith",
            "Engineering",
            "Senior Developer",
            "Jane Manager",
            "Full Time",
            new DateOnly(2026, 1, 1),
            "Active",
            "London",
            "alice@example.com");

    [Fact]
    public async Task HandleAsync_Maps_Request_Filters_Into_ReportFilterCriteria()
    {
        var companyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var employmentTypeId = Guid.NewGuid();
        var dateRangeStart = new DateOnly(2026, 1, 1);
        var dateRangeEnd = new DateOnly(2026, 6, 30);

        var reader = new FakeEmployeeDirectoryReader([]);
        var handler = new GetEmployeeDirectoryReportHandler(reader);

        var request = new GetEmployeeDirectoryReportRequest(
            CompanyId: companyId,
            DepartmentId: departmentId,
            LocationId: locationId,
            PositionProfileId: positionProfileId,
            ManagerId: managerId,
            EmploymentTypeId: employmentTypeId,
            DateRangeStart: dateRangeStart,
            DateRangeEnd: dateRangeEnd,
            EmployeeStatus: "Active",
            Page: 2,
            PageSize: 50,
            SortBy: "startdate",
            SortDescending: true);

        await handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(companyId, reader.LastCompanyId);
        Assert.NotNull(reader.LastFilter);
        Assert.Equal(departmentId, reader.LastFilter!.DepartmentId);
        Assert.Equal(locationId, reader.LastFilter.LocationId);
        Assert.Equal(positionProfileId, reader.LastFilter.PositionProfileId);
        Assert.Equal(managerId, reader.LastFilter.ManagerId);
        Assert.Equal(employmentTypeId, reader.LastFilter.EmploymentTypeId);
        Assert.Equal(dateRangeStart, reader.LastFilter.DateRangeStart);
        Assert.Equal(dateRangeEnd, reader.LastFilter.DateRangeEnd);
        Assert.Equal("Active", reader.LastFilter.EmployeeStatus);

        Assert.NotNull(reader.LastPagination);
        Assert.Equal(2, reader.LastPagination!.PageNumber);
        Assert.Equal(50, reader.LastPagination.PageSize);
        Assert.Equal("startdate", reader.LastSortBy);
        Assert.True(reader.LastSortDescending);
    }

    [Fact]
    public async Task HandleAsync_Maps_PagedResult_Into_Response()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeEmployeeDirectoryReader([BuildItem(employeeId)], totalCount: 42);
        var handler = new GetEmployeeDirectoryReportHandler(reader);

        var request = new GetEmployeeDirectoryReportRequest(
            CompanyId: Guid.NewGuid(),
            DepartmentId: null,
            LocationId: null,
            PositionProfileId: null,
            ManagerId: null,
            EmploymentTypeId: null,
            DateRangeStart: null,
            DateRangeEnd: null,
            EmployeeStatus: null,
            Page: 3,
            PageSize: 10);

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.Equal(42, response.TotalCount);
        Assert.Equal(3, response.Page);
        Assert.Equal(10, response.PageSize);
        var item = Assert.Single(response.Items);
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal("Alice Smith", item.Name);
    }
}
