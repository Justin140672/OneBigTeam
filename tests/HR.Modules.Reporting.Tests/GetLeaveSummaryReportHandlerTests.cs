using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetLeaveSummaryReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetLeaveSummaryReportHandlerTests
{
    [Fact]
    public async Task HandleAsync_Without_LeaveTypeId_Sums_Entitlement_Across_LeaveTypes_When_Grouped_By_Employee()
    {
        // Reproduces the pre-fix behaviour: with no LeaveTypeId filter supplied, grouping by
        // Employee (the default) sums EntitlementDays across every balance-tracked leave type —
        // this is the existing/unchanged shape callers relying on GroupBy=Employee still get, even
        // though it isn't meaningful on its own (hence the new optional filter).
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
        // 25 + 10 + 5 + 52 = 92 — the exact inflated figure from the bug report (Tom Williams).
        Assert.Equal(92m, row.EntitlementDays);
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
}
