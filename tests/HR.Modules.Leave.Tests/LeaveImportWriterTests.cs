using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class LeaveImportWriterTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private const int PolicyYear = 2026; // FixedUtcNow.Year, with default LeaveYearStartMonth = 1

    private static LeaveDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static LeaveImportWriter BuildWriter(
        LeaveDbContext context, FakeCompanyLeaveSettingsReader? settingsReader = null) =>
        new(context, new FakeClock(FixedUtcNow), settingsReader ?? new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());

    private static LeaveType CreateBalanceTrackedLeaveType(Guid companyId, string code = "ANNUAL") =>
        LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", code, 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);

    [Fact]
    public async Task TryLayOpeningBalanceAsync_Returns_False_When_LeaveType_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var writer = BuildWriter(context);

        var result = await writer.TryLayOpeningBalanceAsync(
            companyId, Guid.NewGuid(), "ANNUAL", 20m, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryLayOpeningBalanceAsync_Returns_False_When_LeaveType_Is_Not_Balance_Tracked()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var leaveType = LeaveType.Create(
            Guid.NewGuid(), companyId, "Unpaid Leave", "UNPAID", 0, AccrualMethod.Monthly,
            LeaveTypeBehaviour.Standard, Now, hasBalance: false);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var writer = BuildWriter(context);

        var result = await writer.TryLayOpeningBalanceAsync(
            companyId, Guid.NewGuid(), "UNPAID", 20m, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryLayOpeningBalanceAsync_Returns_False_When_LeaveType_Is_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var leaveType = CreateBalanceTrackedLeaveType(companyId);
        leaveType.Deactivate(Now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var writer = BuildWriter(context);

        var result = await writer.TryLayOpeningBalanceAsync(
            companyId, Guid.NewGuid(), "ANNUAL", 20m, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryLayOpeningBalanceAsync_Returns_False_When_Baseline_Balance_Row_Is_Missing()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var leaveType = CreateBalanceTrackedLeaveType(companyId);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        // No LeaveBalance row seeded for this employee/leave type/policy year.
        var writer = BuildWriter(context);

        var result = await writer.TryLayOpeningBalanceAsync(
            companyId, Guid.NewGuid(), "ANNUAL", 20m, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryLayOpeningBalanceAsync_Adjusts_Balance_To_Match_Requested_Opening_Balance()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var adjustedBy = Guid.NewGuid();

        var leaveType = CreateBalanceTrackedLeaveType(companyId);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 5, false, false, Now);
        // Baseline entitlement of 25 days, no adjustment yet.
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id, PolicyYear, 25m, Now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var writer = BuildWriter(context);

        var result = await writer.TryLayOpeningBalanceAsync(
            companyId, employeeId, "ANNUAL", openingBalanceDays: 18m, adjustedBy, CancellationToken.None);

        Assert.True(result);

        var updatedBalance = await context.LeaveBalances.SingleAsync();
        // 25 (entitlement) + adjustment = 18 => adjustment of -7.
        Assert.Equal(-7m, updatedBalance.AdjustmentDays);
        Assert.Equal(18m, updatedBalance.EntitlementDays + updatedBalance.AdjustmentDays);

        var adjustment = await context.LeaveBalanceAdjustments.SingleAsync();
        Assert.Equal(companyId, adjustment.CompanyId);
        Assert.Equal(employeeId, adjustment.EmployeeId);
        Assert.Equal(leaveType.Id, adjustment.LeaveTypeId);
        Assert.Equal(-7m, adjustment.AdjustmentDays);
        Assert.Null(adjustment.AdjustmentHours);
        Assert.Equal(LeaveBalanceAdjustmentReason.Import, adjustment.Reason);
        Assert.Equal(adjustedBy, adjustment.AdjustedByEmployeeId);
    }

    [Fact]
    public async Task TryLayOpeningBalanceAsync_Is_A_NoOp_When_Requested_Balance_Already_Matches_Current_Total()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateBalanceTrackedLeaveType(companyId);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 5, false, false, Now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id, PolicyYear, 25m, Now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var writer = BuildWriter(context);

        var result = await writer.TryLayOpeningBalanceAsync(
            companyId, employeeId, "ANNUAL", openingBalanceDays: 25m, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result);
        Assert.Empty(context.LeaveBalanceAdjustments);

        var unchangedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, unchangedBalance.AdjustmentDays);
    }

    [Fact]
    public async Task TryLayOpeningBalanceAsync_Returns_False_When_Requested_Code_Case_Does_Not_Match_Stored_Code()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        // LeaveType.Create upper-cases the stored code; the writer's lookup is an exact (not
        // case-insensitive) match, so a lowercase request code will not resolve it.
        var leaveType = CreateBalanceTrackedLeaveType(companyId, code: "annual");
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 5, false, false, Now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id, PolicyYear, 25m, Now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var writer = BuildWriter(context);

        var result = await writer.TryLayOpeningBalanceAsync(
            companyId, employeeId, "annual", openingBalanceDays: 20m, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }
}
