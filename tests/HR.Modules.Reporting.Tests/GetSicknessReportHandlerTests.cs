using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetSicknessReport;
using HR.Modules.Reporting.ReportRegistry;
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
    public async Task HandleAsync_BradfordScore_Is_Spells_Squared_Times_DaysAbsent()
    {
        // SICK-04: Bradford Factor = S^2 * D. One spell (S=1), 2 days absent (D=2) => 1*1*2 = 2.
        var reader = new FakeSicknessReportReader(
        [
            new SicknessReportRecordItem(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 2m),
        ]);
        var handler = new GetSicknessReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetSicknessReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(2, row.BradfordScore);
    }

    [Fact]
    public async Task HandleAsync_BradfordScore_Scales_With_Spell_Count_Squared()
    {
        // Two spells (S=2), 4 total days absent (D=4) => 2^2 * 4 = 16.
        var employeeId = Guid.NewGuid();
        var reader = new FakeSicknessReportReader(
        [
            new SicknessReportRecordItem(employeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3), 3m),
            new SicknessReportRecordItem(employeeId, Guid.NewGuid(), new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), 1m),
        ]);
        var handler = new GetSicknessReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetSicknessReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(16, row.BradfordScore);
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

    [Fact]
    public async Task HandleAsync_Below_DisplayRowLimit_Is_Not_Truncated()
    {
        var records = Enumerable.Range(0, 5)
            .Select(_ => new SicknessReportRecordItem(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 1m))
            .ToList();
        var reader = new FakeSicknessReportReader(records);
        var handler = new GetSicknessReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(new GetSicknessReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsTruncated);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(5, result.Value.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Above_DisplayRowLimit_Is_Truncated_But_Reports_Full_Total()
    {
        // Grouped by employee (default), so distinct employees are needed to exceed the cap.
        const int overLimitBy = 500;
        var totalGroups = ReportLimits.DisplayRowLimit + overLimitBy;
        var records = Enumerable.Range(0, totalGroups)
            .Select(_ => new SicknessReportRecordItem(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 1m))
            .ToList();
        var reader = new FakeSicknessReportReader(records);
        var handler = new GetSicknessReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(new GetSicknessReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsTruncated);
        Assert.Equal(totalGroups, result.Value.TotalCount);
        Assert.Equal(ReportLimits.DisplayRowLimit, result.Value.Items.Count);
    }
}
