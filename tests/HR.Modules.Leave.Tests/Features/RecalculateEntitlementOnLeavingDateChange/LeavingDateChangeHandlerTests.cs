using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.RecalculateEntitlementOnLeavingDateChange;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests.Features.RecalculateEntitlementOnLeavingDateChange;

public class LeavingDateChangeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 11, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_LeavingDateSet_ProRates_Entitlement_Through_Leaving_Date()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var policyYear = LeaveYearCalculator.GetPolicyYear(now, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 1, 1), now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new LeavingDateChangeHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(new DateOnly(2020, 1, 1)));

        // Leaving mid-year (June 30) — roughly half a year's entitlement remains.
        var leavingDate = new DateOnly(2026, 6, 30);
        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, leavingDate, now),
            CancellationToken.None);

        var updated = await context.LeaveBalances.SingleAsync();
        Assert.True(updated.EntitlementDays < 25m);
        Assert.True(updated.EntitlementDays > 0m);
    }

    [Fact]
    public async Task HandleAsync_Amended_LeavingDate_Recalculates_Rather_Than_Compounds()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var policyYear = LeaveYearCalculator.GetPolicyYear(now, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 1, 1), now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new LeavingDateChangeHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(new DateOnly(2020, 1, 1)));

        // Set an initial leaving date, then amend it earlier — applying repeated events must
        // converge on the figure implied by the *current* leaving date, never stack reductions.
        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, new DateOnly(2026, 9, 30), now),
            CancellationToken.None);
        var afterFirst = (await context.LeaveBalances.SingleAsync()).EntitlementDays;

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, new DateOnly(2026, 3, 31), now),
            CancellationToken.None);
        var afterAmend = (await context.LeaveBalances.SingleAsync()).EntitlementDays;

        Assert.True(afterAmend < afterFirst);

        // Re-delivering the same (amended) event again must be a no-op relative to the current value.
        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, new DateOnly(2026, 3, 31), now),
            CancellationToken.None);
        var afterRedelivery = (await context.LeaveBalances.SingleAsync()).EntitlementDays;

        Assert.Equal(afterAmend, afterRedelivery);
    }

    [Fact]
    public async Task HandleAsync_LeavingProcessCancelled_Restores_Full_Entitlement()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var policyYear = LeaveYearCalculator.GetPolicyYear(now, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 1, 1), now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new LeavingDateChangeHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(new DateOnly(2020, 1, 1)));

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, new DateOnly(2026, 3, 31), now),
            CancellationToken.None);
        var reduced = (await context.LeaveBalances.SingleAsync()).EntitlementDays;
        Assert.True(reduced < 25m);

        await handler.HandleAsync(
            new EmployeeLeavingProcessCancelledIntegrationEvent(companyId, employeeId, now),
            CancellationToken.None);

        var restored = await context.LeaveBalances.SingleAsync();
        Assert.Equal(25m, restored.EntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_Preserves_Usage_And_Manual_Adjustment_On_Reduction()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var policyYear = LeaveYearCalculator.GetPolicyYear(now, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 1, 1), now);
        balance.RecordUsage(20m, now);
        balance.Adjust(2m, now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new LeavingDateChangeHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(new DateOnly(2020, 1, 1)));

        // Leaving very early in the year drives entitlement below the already-recorded usage,
        // producing a legitimate negative remaining balance that must not be clamped.
        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, new DateOnly(2026, 1, 31), now),
            CancellationToken.None);

        var updated = await context.LeaveBalances.SingleAsync();
        Assert.Equal(20m, updated.UsedDays);
        Assert.Equal(2m, updated.AdjustmentDays);
        Assert.True(updated.EntitlementDays < 20m);
        Assert.True(updated.RemainingDays < 0m);
    }

    [Fact]
    public async Task HandleAsync_Never_Recalculates_Toil_Balances()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var policyYear = LeaveYearCalculator.GetPolicyYear(now, startMonth: 1);

        var toilType = LeaveType.Create(Guid.NewGuid(), companyId, "Time Off In Lieu", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, now);
        context.LeaveTypes.Add(toilType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, toilType.Id,
            Guid.NewGuid(), policyYear, 0m, new DateOnly(2026, 1, 1), now);
        balance.Adjust(4m, now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new LeavingDateChangeHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(new DateOnly(2020, 1, 1)));

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, new DateOnly(2026, 3, 31), now),
            CancellationToken.None);

        var unchanged = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, unchanged.EntitlementDays);
        Assert.Equal(4m, unchanged.AdjustmentDays);
    }

    [Fact]
    public async Task HandleAsync_Does_Nothing_When_Employee_StartDate_Cannot_Be_Resolved()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var policyYear = LeaveYearCalculator.GetPolicyYear(now, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 1, 1), now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new LeavingDateChangeHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(null));

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, new DateOnly(2026, 3, 31), now),
            CancellationToken.None);

        var unchanged = await context.LeaveBalances.SingleAsync();
        Assert.Equal(25m, unchanged.EntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_Backdated_LeavingDate_ProRates_Correctly()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var policyYear = LeaveYearCalculator.GetPolicyYear(now, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 24,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 24m, new DateOnly(2026, 1, 1), now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new LeavingDateChangeHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(new DateOnly(2020, 1, 1)));

        // Leaving date backdated to a date already in the past relative to "now" — half the year
        // (Jan 1 - Jun 30 inclusive, 181 of 365 days).
        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 30), now),
            CancellationToken.None);

        var updated = await context.LeaveBalances.SingleAsync();
        Assert.Equal(24m * 181 / 365, updated.EntitlementDays);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
