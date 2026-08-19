using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetHrHeadcountSummaryReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetHrHeadcountSummaryReportHandlerTests
{
    [Fact]
    public async Task HandleAsync_Maps_Request_Filters_Into_ReportFilterCriteria()
    {
        var companyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var employmentTypeId = Guid.NewGuid();
        var reader = new FakeHrHeadcountSummaryReader();
        var handler = new GetHrHeadcountSummaryReportHandler(reader);

        var request = new GetHrHeadcountSummaryReportRequest(
            CompanyId: companyId,
            DepartmentId: departmentId,
            LocationId: locationId,
            EmploymentTypeId: employmentTypeId,
            EmployeeStatus: "Active");

        await handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(companyId, reader.LastCompanyId);
        Assert.NotNull(reader.LastFilter);
        Assert.Equal(departmentId, reader.LastFilter!.DepartmentId);
        Assert.Equal(locationId, reader.LastFilter.LocationId);
        Assert.Equal(employmentTypeId, reader.LastFilter.EmploymentTypeId);
        Assert.Equal("Active", reader.LastFilter.EmployeeStatus);
    }

    [Fact]
    public async Task HandleAsync_Maps_Reader_Result_Into_Response()
    {
        var employeeId = Guid.NewGuid();
        var items = new List<HrHeadcountSummaryItem>
        {
            new(
                employeeId,
                "Alice Smith",
                "Engineering",
                "London",
                "Senior Developer",
                "Full Time",
                "Active",
                new DateOnly(2026, 1, 1),
                null,
                1.0m),
        };
        var reader = new FakeHrHeadcountSummaryReader(
            new HrHeadcountSummaryResult(items, TotalHeadcount: 1, ActiveEmployees: 1, FutureStarters: 0, Leavers: 0, TotalFte: 1.0m));
        var handler = new GetHrHeadcountSummaryReportHandler(reader);

        var result = await handler.HandleAsync(
            new GetHrHeadcountSummaryReportRequest(Guid.NewGuid(), null, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.Equal(1, response.TotalHeadcount);
        Assert.Equal(1, response.ActiveEmployees);
        Assert.Equal(0, response.FutureStarters);
        Assert.Equal(0, response.Leavers);
        Assert.Equal(1.0m, response.TotalFte);
        var item = Assert.Single(response.Items);
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal("Alice Smith", item.EmployeeName);
    }

    [Fact]
    public async Task HandleAsync_Allows_Null_Filters()
    {
        var reader = new FakeHrHeadcountSummaryReader();
        var handler = new GetHrHeadcountSummaryReportHandler(reader);

        await handler.HandleAsync(
            new GetHrHeadcountSummaryReportRequest(Guid.NewGuid(), null, null, null, null), CancellationToken.None);

        Assert.NotNull(reader.LastFilter);
        Assert.Null(reader.LastFilter!.DepartmentId);
        Assert.Null(reader.LastFilter.LocationId);
        Assert.Null(reader.LastFilter.EmploymentTypeId);
        Assert.Null(reader.LastFilter.EmployeeStatus);
    }
}
