using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetLeaveSummaryReport;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetLeaveSummaryReportHandlerTests
{
    [Fact]
    public async Task HandleAsync_Without_LeaveTypeId_Reflects_Annual_Leave_Only_When_Grouped_By_Employee()
    {
        // Regression test for the real bug: grouping by Employee with no LeaveTypeId filter used
        // to sum EntitlementDays across EVERY balance-tracked leave type for that employee
        // (25 Annual + 10 Sick + 5 Compassionate + 52 Parental = 92) — a meaningless combined
        // figure, not a genuine entitlement anyone has. Fixed to restrict to Annual Leave (the
        // one entitlement-bearing "headline" leave type) when no explicit filter narrows it.
        var employeeId = Guid.NewGuid();
        var annualTypeId = Guid.NewGuid();
        var sickTypeId = Guid.NewGuid();
        var compassionateTypeId = Guid.NewGuid();
        var parentalTypeId = Guid.NewGuid();
        var reader = new FakeLeaveSummaryReader(
        [
            new LeaveSummaryReportRow(employeeId, annualTypeId, "Annual Leave", 25m, 5m, 5m, 20m, 1),
            new LeaveSummaryReportRow(employeeId, sickTypeId, "Sick Leave", 10m, 0m, 0m, 10m, 0),
            new LeaveSummaryReportRow(employeeId, compassionateTypeId, "Compassionate Leave", 5m, 0m, 0m, 5m, 0),
            new LeaveSummaryReportRow(employeeId, parentalTypeId, "Parental Leave", 52m, 0m, 0m, 52m, 0),
        ]);
        var handler = new GetLeaveSummaryReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new GetLeaveSummaryReportRequest(Guid.NewGuid()),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(employeeId.ToString(), row.GroupKey);
        Assert.Equal(25m, row.EntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_Without_LeaveTypeId_Reflects_Annual_Leave_Only_When_Grouped_By_Department()
    {
        var employeeId = Guid.NewGuid();
        var annualTypeId = Guid.NewGuid();
        var sickTypeId = Guid.NewGuid();
        var reader = new FakeLeaveSummaryReader(
        [
            new LeaveSummaryReportRow(employeeId, annualTypeId, "Annual Leave", 23m, 0m, 0m, 23m, 0),
            new LeaveSummaryReportRow(employeeId, sickTypeId, "Sick Leave", 10m, 0m, 0m, 10m, 0),
        ]);
        var handler = new GetLeaveSummaryReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new GetLeaveSummaryReportRequest(Guid.NewGuid(), GroupBy: LeaveSummaryGroupBy.Department),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(23m, row.EntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_With_LeaveTypeId_Only_Includes_That_LeaveType_Entitlement()
    {
        var employeeId = Guid.NewGuid();
        var annualTypeId = Guid.NewGuid();
        var sickTypeId = Guid.NewGuid();
        var compassionateTypeId = Guid.NewGuid();
        var parentalTypeId = Guid.NewGuid();
        var reader = new FakeLeaveSummaryReader(
        [
            new LeaveSummaryReportRow(employeeId, annualTypeId, "Annual Leave", 25m, 5m, 5m, 20m, 1),
            new LeaveSummaryReportRow(employeeId, sickTypeId, "Sick Leave", 10m, 0m, 0m, 10m, 0),
            new LeaveSummaryReportRow(employeeId, compassionateTypeId, "Compassionate Leave", 5m, 0m, 0m, 5m, 0),
            new LeaveSummaryReportRow(employeeId, parentalTypeId, "Parental Leave", 52m, 0m, 0m, 52m, 0),
        ]);
        var handler = new GetLeaveSummaryReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new GetLeaveSummaryReportRequest(Guid.NewGuid(), LeaveTypeId: annualTypeId),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(employeeId.ToString(), row.GroupKey);
        // Only Annual Leave's entitlement (25 days) should be reflected — not the 92 combined total.
        Assert.Equal(25m, row.EntitlementDays);
        Assert.Equal(5m, row.BookedDays);
        Assert.Equal(5m, row.ApprovedDays);
        Assert.Equal(20m, row.RemainingDays);
        Assert.Equal(1, row.PendingRequestCount);
    }

    [Fact]
    public async Task HandleAsync_With_LeaveTypeId_Excludes_Employees_With_No_Balance_For_That_LeaveType()
    {
        var employeeWithAnnual = Guid.NewGuid();
        var employeeWithSickOnly = Guid.NewGuid();
        var annualTypeId = Guid.NewGuid();
        var sickTypeId = Guid.NewGuid();
        var reader = new FakeLeaveSummaryReader(
        [
            new LeaveSummaryReportRow(employeeWithAnnual, annualTypeId, "Annual Leave", 25m, 0m, 0m, 25m, 0),
            new LeaveSummaryReportRow(employeeWithSickOnly, sickTypeId, "Sick Leave", 10m, 0m, 0m, 10m, 0),
        ]);
        var handler = new GetLeaveSummaryReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new GetLeaveSummaryReportRequest(Guid.NewGuid(), LeaveTypeId: annualTypeId),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(employeeWithAnnual.ToString(), row.GroupKey);
    }

    [Fact]
    public async Task HandleAsync_Below_DisplayRowLimit_Is_Not_Truncated()
    {
        var annualTypeId = Guid.NewGuid();
        var rows = Enumerable.Range(0, 5)
            .Select(_ => new LeaveSummaryReportRow(Guid.NewGuid(), annualTypeId, "Annual Leave", 25m, 0m, 0m, 25m, 0))
            .ToList();
        var reader = new FakeLeaveSummaryReader(rows);
        var handler = new GetLeaveSummaryReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new GetLeaveSummaryReportRequest(Guid.NewGuid()),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsTruncated);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(5, result.Value.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Above_DisplayRowLimit_Is_Truncated_But_Reports_Full_Total()
    {
        const int overLimitBy = 500;
        var totalGroups = ReportLimits.DisplayRowLimit + overLimitBy;
        var annualTypeId = Guid.NewGuid();
        var rows = Enumerable.Range(0, totalGroups)
            .Select(_ => new LeaveSummaryReportRow(Guid.NewGuid(), annualTypeId, "Annual Leave", 25m, 0m, 0m, 25m, 0))
            .ToList();
        var reader = new FakeLeaveSummaryReader(rows);
        var handler = new GetLeaveSummaryReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new GetLeaveSummaryReportRequest(Guid.NewGuid()),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsTruncated);
        Assert.Equal(totalGroups, result.Value.TotalCount);
        Assert.Equal(ReportLimits.DisplayRowLimit, result.Value.Items.Count);
    }
}
