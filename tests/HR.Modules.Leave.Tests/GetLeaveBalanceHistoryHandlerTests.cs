using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.GetLeaveBalanceHistory;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class GetLeaveBalanceHistoryHandlerTests
{
    // All seeded events fall within 2026, and "now" is fixed inside 2026 too so the handler's
    // "current policy year" (calendar year, since LeaveYearStartMonth defaults to 1) lines up
    // with the seeded LeaveBalance's PolicyYear, making the BalanceAfter running-total math
    // deterministic in these tests.
    private static readonly DateTime FixedUtcNow = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTimeOffset ApprovedLeaveDate = new(2026, 1, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CancelledLeaveDate = new(2026, 2, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ToilAwardDate = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ManualAdjustmentDate = new(2026, 4, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CarryOverDate = new(2026, 5, 10, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid ApproverId = Guid.NewGuid();
    private static readonly Guid ToilAwarderId = Guid.NewGuid();
    private static readonly Guid AdjusterId = Guid.NewGuid();

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    private static GetLeaveBalanceHistoryHandler BuildHandler(
        LeaveDbContext context, WorkingPattern? pattern = null, Dictionary<Guid, string>? names = null) =>
        new(
            context,
            new FakeWorkingPatternProvider(pattern),
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeNameReader(names));

    /// <summary>
    /// Seeds one leave type, an employee's balance for the current (2026) policy year, and one
    /// event of each of the 5 history categories. The balance's Entitlement/Used/Adjustment
    /// remain at their initial Create() values (25/0/0) regardless of the history events seeded
    /// alongside it — this test seeds history rows directly rather than driving them through the
    /// real handlers, so the "current remaining balance" anchor is simply whatever the
    /// LeaveBalance row says, not a derived total of the seeded events.
    /// </summary>
    private static (Guid CompanyId, Guid EmployeeId, Guid LeaveTypeId, Guid LeaveBalanceId) SeedFullHistory(
        LeaveDbContext context, Guid? companyId = null, Guid? employeeId = null, Guid? leaveTypeId = null,
        string leaveTypeName = "Annual Leave")
    {
        var company = companyId ?? Guid.NewGuid();
        var employee = employeeId ?? Guid.NewGuid();
        var leaveType = leaveTypeId ?? Guid.NewGuid();
        var policyId = Guid.NewGuid();

        // Check both already-tracked-but-unsaved entries (Local) and previously persisted rows,
        // since these test helpers may be called multiple times against the same context before
        // a single SaveChangesAsync() at the end (see the "noise" seeding tests below).
        if (!context.LeaveTypes.Local.Any(t => t.Id == leaveType) && !context.LeaveTypes.Any(t => t.Id == leaveType))
        {
            context.LeaveTypes.Add(LeaveType.Create(
                leaveType, company, leaveTypeName, leaveTypeName.ToUpperInvariant(),
                25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, ApprovedLeaveDate));
        }

        var balance = LeaveBalance.Create(Guid.NewGuid(), company, employee, leaveType, policyId, 2026, 25m, new DateOnly(2026, 1, 1), ApprovedLeaveDate);
        context.LeaveBalances.Add(balance);

        var approved = LeaveRequest.Create(
            Guid.NewGuid(), company, employee, leaveType, policyId,
            new DateOnly(2026, 1, 5), LeaveDayPart.FullDay, new DateOnly(2026, 1, 9), LeaveDayPart.FullDay,
            4m, "Family trip", ApprovedLeaveDate);
        approved.Approve(ApproverId, ApprovedLeaveDate);
        context.LeaveRequests.Add(approved);

        var cancelled = LeaveRequest.Create(
            Guid.NewGuid(), company, employee, leaveType, policyId,
            new DateOnly(2026, 2, 5), LeaveDayPart.FullDay, new DateOnly(2026, 2, 6), LeaveDayPart.FullDay,
            2m, "Changed plans", CancelledLeaveDate);
        cancelled.Cancel(CancelledLeaveDate);
        context.LeaveRequests.Add(cancelled);

        var toilTransaction = ToilTransaction.CreateEarned(
            Guid.NewGuid(), company, employee, balance.Id, ToilAwarderId,
            1m, new DateOnly(2026, 3, 8), null, "Overtime", ToilAwardDate);
        context.ToilTransactions.Add(toilTransaction);

        // Adjustment amounts are stored in days (AdjustmentDays); AdjustmentHours is only
        // populated for TOIL-behaviour leave types, which this Standard leave type is not.
        var manualAdjustment = LeaveBalanceAdjustment.Create(
            Guid.NewGuid(), company, employee, leaveType,
            2m, null, LeaveBalanceAdjustmentReason.ManualAward, "Bonus days", AdjusterId, ManualAdjustmentDate);
        context.LeaveBalanceAdjustments.Add(manualAdjustment);

        var carryOver = LeaveBalanceAdjustment.Create(
            Guid.NewGuid(), company, employee, leaveType,
            1m, null, LeaveBalanceAdjustmentReason.CarryOver, null, AdjusterId, CarryOverDate);
        context.LeaveBalanceAdjustments.Add(carryOver);

        return (company, employee, leaveType, balance.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_All_Five_Categories_Sorted_By_Date_Descending()
    {
        await using var context = BuildContext();
        var (companyId, employeeId, leaveTypeId, _) = SeedFullHistory(context);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(
            new GetLeaveBalanceHistoryRequest(companyId, employeeId, leaveTypeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items;
        Assert.Equal(5, items.Count);

        Assert.Equal(["CarryOver", "ManualAdjustment", "ToilAward", "CancelledLeave", "ApprovedLeave"],
            items.Select(i => i.Category).ToArray());

        Assert.True(items.SequenceEqual(items.OrderByDescending(i => i.Date)));
        Assert.All(items, i => Assert.Equal("Annual Leave", i.LeaveTypeName));
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_LeaveType_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            new GetLeaveBalanceHistoryRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Applies_Signed_Change_Convention_Per_Category()
    {
        await using var context = BuildContext();
        var (companyId, employeeId, leaveTypeId, _) = SeedFullHistory(context);
        await context.SaveChangesAsync();

        var customPattern = new WorkingPattern(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday, 8m);
        var handler = BuildHandler(context, customPattern);

        var result = await handler.HandleAsync(
            new GetLeaveBalanceHistoryRequest(companyId, employeeId, leaveTypeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items;

        // Approved leave consumes balance -> negative Change.
        var approved = Assert.Single(items, i => i.Category == "ApprovedLeave");
        Assert.Equal(-32m, approved.Change); // -(4 days * 8 hours/day)
        Assert.Equal("Leave Taken", approved.Reason);

        // Cancelled leave gives hours back -> positive Change.
        var cancelled = Assert.Single(items, i => i.Category == "CancelledLeave");
        Assert.Equal(16m, cancelled.Change); // 2 days * 8 hours/day
        Assert.Equal("Leave Cancelled", cancelled.Reason);

        // TOIL award adds to balance -> positive Change.
        var toil = Assert.Single(items, i => i.Category == "ToilAward");
        Assert.Equal(8m, toil.Change); // 1 day * 8 hours/day
        Assert.Equal("TOIL Award", toil.Reason);

        // Adjustment days are converted to hours via the employee's working pattern; sign is
        // the adjustment's own signed value.
        var manual = Assert.Single(items, i => i.Category == "ManualAdjustment");
        Assert.Equal(16m, manual.Change); // 2 days * 8 hours/day
        Assert.Equal("ManualAward", manual.Reason);

        var carryOver = Assert.Single(items, i => i.Category == "CarryOver");
        Assert.Equal(8m, carryOver.Change); // 1 day * 8 hours/day
        Assert.Equal("Carry Over", carryOver.Reason);
    }

    [Fact]
    public async Task HandleAsync_Computes_BalanceAfter_As_Running_Total_Anchored_To_Current_Balance()
    {
        await using var context = BuildContext();
        var (companyId, employeeId, leaveTypeId, _) = SeedFullHistory(context);
        await context.SaveChangesAsync();

        // Default working pattern (7.5 hours/day). The seeded LeaveBalance is untouched by the
        // history rows (Entitlement 25, Used 0, Adjustment 0 -> Remaining 25 days = 187.5 hours).
        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(
            new GetLeaveBalanceHistoryRequest(companyId, employeeId, leaveTypeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items;

        // Ascending chronological order (default working pattern, 7.5 hours/day):
        // Approved(-30) -> Cancelled(+15) -> Toil(+7.5) -> ManualAward(2 days = +15) ->
        // CarryOver(1 day = +7.5). Starting balance = 187.5 - 15 = 172.5.
        var byCategory = items.ToDictionary(i => i.Category);
        Assert.Equal(142.5m, byCategory["ApprovedLeave"].BalanceAfter);   // 172.5 - 30
        Assert.Equal(157.5m, byCategory["CancelledLeave"].BalanceAfter);  // 142.5 + 15
        Assert.Equal(165m, byCategory["ToilAward"].BalanceAfter);        // 157.5 + 7.5
        Assert.Equal(180m, byCategory["ManualAdjustment"].BalanceAfter); // 165 + 15
        Assert.Equal(187.5m, byCategory["CarryOver"].BalanceAfter);      // 180 + 7.5 == current remaining balance
    }

    [Fact]
    public async Task HandleAsync_Resolves_CreatedBy_Display_Names_Per_Actor()
    {
        await using var context = BuildContext();
        var (companyId, employeeId, leaveTypeId, _) = SeedFullHistory(context);
        await context.SaveChangesAsync();

        var names = new Dictionary<Guid, string>
        {
            [ApproverId] = "Approver Name",
            [ToilAwarderId] = "Awarder Name",
            [AdjusterId] = "Adjuster Name",
            [employeeId] = "Employee Name",
        };

        var handler = BuildHandler(context, names: names);
        var result = await handler.HandleAsync(
            new GetLeaveBalanceHistoryRequest(companyId, employeeId, leaveTypeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items.ToDictionary(i => i.Category);

        Assert.Equal("Approver Name", items["ApprovedLeave"].CreatedBy);
        Assert.Equal("Employee Name", items["CancelledLeave"].CreatedBy); // self-service cancellation, no separate actor tracked
        Assert.Equal("Awarder Name", items["ToilAward"].CreatedBy);
        Assert.Equal("Adjuster Name", items["ManualAdjustment"].CreatedBy);
        Assert.Equal("Adjuster Name", items["CarryOver"].CreatedBy);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Unknown_Employee_When_Actor_Name_Not_Found()
    {
        await using var context = BuildContext();
        var (companyId, employeeId, leaveTypeId, _) = SeedFullHistory(context);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context); // no names supplied
        var result = await handler.HandleAsync(
            new GetLeaveBalanceHistoryRequest(companyId, employeeId, leaveTypeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value!.Items, i => Assert.Equal("Unknown Employee", i.CreatedBy));
    }

    [Fact]
    public async Task HandleAsync_Excludes_Pending_And_Rejected_Leave_Requests()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        context.LeaveTypes.Add(LeaveType.Create(
            leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now));

        var pending = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId,
            new DateOnly(2026, 6, 1), LeaveDayPart.FullDay, new DateOnly(2026, 6, 2), LeaveDayPart.FullDay,
            2m, null, now);
        context.LeaveRequests.Add(pending);

        var rejected = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId,
            new DateOnly(2026, 7, 1), LeaveDayPart.FullDay, new DateOnly(2026, 7, 2), LeaveDayPart.FullDay,
            2m, null, now);
        rejected.Reject(Guid.NewGuid(), now, "Not approved");
        context.LeaveRequests.Add(rejected);

        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(
            new GetLeaveBalanceHistoryRequest(companyId, employeeId, leaveTypeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Only_Includes_Matching_Company_Employee_And_LeaveType()
    {
        await using var context = BuildContext();
        var (companyId, employeeId, leaveTypeId, _) = SeedFullHistory(context);

        // Noise: same employee/leave type but different company.
        SeedFullHistory(context, companyId: Guid.NewGuid(), employeeId: employeeId, leaveTypeId: leaveTypeId);

        // Noise: same company/leave type but different employee.
        SeedFullHistory(context, companyId: companyId, employeeId: Guid.NewGuid(), leaveTypeId: leaveTypeId);

        // Noise: same company/employee but different leave type.
        SeedFullHistory(context, companyId: companyId, employeeId: employeeId, leaveTypeId: Guid.NewGuid(), leaveTypeName: "Sick Leave");

        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(
            new GetLeaveBalanceHistoryRequest(companyId, employeeId, leaveTypeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Items.Count);
    }
}
