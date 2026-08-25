using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Reporting.Features.GetLeaveCalendarReport;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetLeaveCalendarReportHandlerTests
{
    private static LeaveCalendarReportItem BuildItem(Guid employeeId) =>
        new(employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 5), "Annual Leave", 5m, "Approved");

    [Fact]
    public async Task HandleAsync_Below_DisplayRowLimit_Is_Not_Truncated()
    {
        var items = Enumerable.Range(0, 5).Select(_ => BuildItem(Guid.NewGuid())).ToList();
        var reader = new FakeLeaveCalendarReader(items);
        var handler = new GetLeaveCalendarReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(new GetLeaveCalendarReportRequest(Guid.NewGuid(), 2026, 8), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsTruncated);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(5, result.Value.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Above_DisplayRowLimit_Is_Truncated_But_Reports_Full_Total()
    {
        const int overLimitBy = 500;
        var totalItems = ReportLimits.DisplayRowLimit + overLimitBy;
        var items = Enumerable.Range(0, totalItems).Select(_ => BuildItem(Guid.NewGuid())).ToList();
        var reader = new FakeLeaveCalendarReader(items);
        var handler = new GetLeaveCalendarReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(new GetLeaveCalendarReportRequest(Guid.NewGuid(), 2026, 8), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsTruncated);
        Assert.Equal(totalItems, result.Value.TotalCount);
        Assert.Equal(ReportLimits.DisplayRowLimit, result.Value.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Department_Filter_Applied_Before_Truncation_Cap()
    {
        // Regression guard: DepartmentId filtering must narrow the set BEFORE totalCount/cap are
        // computed, so a company with far more than DisplayRowLimit total rows, but fewer than the
        // cap in the requested department, must NOT be reported as truncated, and TotalCount must
        // reflect the filtered (department-scoped) count, not the unfiltered company-wide count.
        var matchingDepartmentId = Guid.NewGuid();
        var otherDepartmentId = Guid.NewGuid();
        var matchingEmployeeId = Guid.NewGuid();

        const int nonMatchingCount = 30_000; // exceeds DisplayRowLimit on its own
        var nonMatchingItems = Enumerable.Range(0, nonMatchingCount).Select(_ => BuildItem(Guid.NewGuid())).ToList();
        var matchingItem = BuildItem(matchingEmployeeId);
        var allItems = nonMatchingItems.Append(matchingItem).ToList();

        var reader = new FakeLeaveCalendarReader(allItems);
        var departments = new Dictionary<Guid, EmployeeDepartmentInfo>
        {
            [matchingEmployeeId] = new(matchingEmployeeId, "Match", matchingDepartmentId, "Engineering"),
        };
        // Every non-matching employee falls back to "no department entry" (department reader
        // returns nothing for them), so they are excluded when DepartmentId is supplied.
        var handler = new GetLeaveCalendarReportHandler(reader, new FakeEmployeeDepartmentReader(departments));

        var result = await handler.HandleAsync(
            new GetLeaveCalendarReportRequest(Guid.NewGuid(), 2026, 8, DepartmentId: matchingDepartmentId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsTruncated);
        Assert.Equal(1, result.Value.TotalCount);
        var row = Assert.Single(result.Value.Items);
        Assert.Equal(matchingEmployeeId, row.EmployeeId);
        _ = otherDepartmentId; // documents intent: never assigned, so those rows are excluded by omission
    }
}
