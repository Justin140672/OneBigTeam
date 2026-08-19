using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetSicknessReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetSicknessReportHandlerTests
{
    [Fact]
    public async Task HandleAsync_Groups_By_Employee_By_Default()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeSicknessReportReader(
        [
            new SicknessReportRecordItem(employeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3), 3m),
            new SicknessReportRecordItem(employeeId, Guid.NewGuid(), new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), 1m),
        ]);
        var handler = new GetSicknessReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetSicknessReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(employeeId.ToString(), row.GroupKey);
        Assert.Equal(2, row.AbsenceCount);
        Assert.Equal(4m, row.DaysAbsent);
    }

    [Fact]
    public async Task HandleAsync_BradfordScore_Is_Always_Zero()
    {
        var reader = new FakeSicknessReportReader(
        [
            new SicknessReportRecordItem(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 2m),
        ]);
        var handler = new GetSicknessReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetSicknessReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(0, row.BradfordScore);
    }

    [Fact]
    public async Task HandleAsync_Groups_By_Department_When_Requested()
    {
        var employeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var reader = new FakeSicknessReportReader(
        [
            new SicknessReportRecordItem(employeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 1m),
        ]);
        var departments = new Dictionary<Guid, EmployeeDepartmentInfo>
        {
            [employeeId] = new(employeeId, "Alice", departmentId, "Engineering"),
        };
        var handler = new GetSicknessReportHandler(reader, new FakeEmployeeDepartmentReader(departments));

        var result = await handler.HandleAsync(
            new GetSicknessReportRequest(companyId, GroupBy: SicknessReportGroupBy.Department), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(departmentId.ToString(), row.GroupKey);
        Assert.Equal("Engineering", row.GroupLabel);
    }

    [Fact]
    public async Task HandleAsync_Passes_Date_Range_To_Reader()
    {
        var reader = new FakeSicknessReportReader([]);
        var handler = new GetSicknessReportHandler(reader, new FakeEmployeeDepartmentReader());
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 1, 31);
        var companyId = Guid.NewGuid();

        await handler.HandleAsync(
            new GetSicknessReportRequest(companyId, start, end), CancellationToken.None);

        Assert.Equal(companyId, reader.LastCompanyId);
        Assert.Equal(start, reader.LastStartDate);
        Assert.Equal(end, reader.LastEndDate);
    }
}
