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

    private static (Guid CompanyId, Guid EmployeeId, Guid LeaveTypeId, Guid LeaveBalanceId) SeedFullHistory(
        LeaveDbContext context, Guid? companyId = null, Guid? employeeId = null, Guid? leaveTypeId = null,
        string leaveTypeName = "Annual Leave")
    {
        var company = companyId ?? Guid.NewGuid();
        var employee = employeeId ?? Guid.NewGuid();
        var leaveType = leaveTypeId ?? Guid.NewGuid();
        var policyId = Guid.NewGuid();

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

        var approved = Assert.Single(items, i => i.Category == "ApprovedLeave");
        Assert.Equal(-32m, approved.Change);
        Assert.Equal("Leave Taken", approved.Reason);

        var cancelled = Assert.Single(items, i => i.Category == "CancelledLeave");
        Assert.Equal(16m, cancelled.Change);
        Assert.Equal("Leave Cancelled", cancelled.Reason);

        var toil = Assert.Single(items, i => i.Category == "ToilAward");
        Assert.Equal(8m, toil.Change);
        Assert.Equal("TOIL Award", toil.Reason);

        var manual = Assert.Single(items, i => i.Category == "ManualAdjustment");
        Assert.Equal(16m, manual.Change);
        Assert.Equal("ManualAward", manual.Reason);

        var carryOver = Assert.Single(items, i => i.Category == "CarryOver");
        Assert.Equal(8m, carryOver.Change);
        Assert.Equal("Carry Over", carryOver.Reason);
    }

    [Fact]
    public async Task HandleAsync_Computes_BalanceAfter_As_Running_Total_Anchored_To_Current_Balance()
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

        var byCategory = items.ToDictionary(i => i.Category);
        Assert.Equal(142.5m, byCategory["ApprovedLeave"].BalanceAfter);
        Assert.Equal(157.5m, byCategory["CancelledLeave"].BalanceAfter);
        Assert.Equal(165m, byCategory["ToilAward"].BalanceAfter);
        Assert.Equal(180m, byCategory["ManualAdjustment"].BalanceAfter);
        Assert.Equal(187.5m, byCategory["CarryOver"].BalanceAfter);
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
        Assert.Equal("Employee Name", items["CancelledLeave"].CreatedBy);
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

        var handler = BuildHandler(context);
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

        SeedFullHistory(context, companyId: Guid.NewGuid(), employeeId: employeeId, leaveTypeId: leaveTypeId);

        SeedFullHistory(context, companyId: companyId, employeeId: Guid.NewGuid(), leaveTypeId: leaveTypeId);

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
