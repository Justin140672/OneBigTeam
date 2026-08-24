using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.RecalculateEntitlementOnLeavingDateChange;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class LeavingDateChangeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 11, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_LeavingDateSet_Reduces_Entitlement_For_MidYear_Leaver()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var leavingDate = new DateOnly(2026, 6, 30);
        var policyYear = LeaveYearCalculator.GetPolicyYear(leavingDate, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 1, 1), now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new DateOnly(2020, 1, 1));

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, leavingDate, now),
            CancellationToken.None);

        var updated = await context.LeaveBalances.SingleAsync();
        Assert.Equal(12.4m, updated.EntitlementDays); // 25 * 181 / 365 = 12.397... rounded to 12.4
    }

    [Fact]
    public async Task HandleAsync_LeavingDateSet_Twice_Recalculates_Rather_Than_Compounding()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var firstLeavingDate = new DateOnly(2026, 6, 30);
        var policyYear = LeaveYearCalculator.GetPolicyYear(firstLeavingDate, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 1, 1), now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new DateOnly(2020, 1, 1));

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, firstLeavingDate, now),
            CancellationToken.None);

        var amendedLeavingDate = new DateOnly(2026, 9, 30);
        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, amendedLeavingDate, now),
            CancellationToken.None);

        var updated = await context.LeaveBalances.SingleAsync();
        Assert.Equal(18.7m, updated.EntitlementDays); // 25 * 273 / 365 = 18.6986... rounded to 18.70
    }

    [Fact]
    public async Task HandleAsync_LeavingProcessCancelled_Restores_Entitlement_To_PreLeaving_Figure()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var leavingDate = new DateOnly(2026, 6, 30);
        var policyYear = LeaveYearCalculator.GetPolicyYear(leavingDate, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 1, 1), now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new DateOnly(2020, 1, 1));

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, leavingDate, now),
            CancellationToken.None);

        var reduced = await context.LeaveBalances.SingleAsync();
        Assert.Equal(12.4m, reduced.EntitlementDays);

        // Cancellation targets the *current* policy year (which, with FixedUtcNow of 2026-06-11,
        // is the same policy year the leaving date fell in).
        await handler.HandleAsync(
            new EmployeeLeavingProcessCancelledIntegrationEvent(companyId, employeeId, now),
            CancellationToken.None);

        var restored = await context.LeaveBalances.SingleAsync();
        Assert.Equal(25m, restored.EntitlementDays); // started well before the year -> full entitlement
    }

    [Fact]
    public async Task HandleAsync_Recalculates_EntitlementDays_Even_When_UsedDays_And_AdjustmentDays_Are_Recorded()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var leavingDate = new DateOnly(2026, 6, 30);
        var policyYear = LeaveYearCalculator.GetPolicyYear(leavingDate, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 1, 1), now);
        balance.RecordUsage(3m, now);
        balance.Adjust(1m, now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new DateOnly(2020, 1, 1));

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, leavingDate, now),
            CancellationToken.None);

        var updated = await context.LeaveBalances.SingleAsync();
        Assert.Equal(12.4m, updated.EntitlementDays);
        Assert.Equal(3m, updated.UsedDays);
        Assert.Equal(1m, updated.AdjustmentDays);
    }

    [Fact]
    public async Task HandleAsync_Allows_RemainingDays_To_Go_Negative_When_Reduced_Entitlement_Is_Below_UsedDays()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var leavingDate = new DateOnly(2026, 1, 10);
        var policyYear = LeaveYearCalculator.GetPolicyYear(leavingDate, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 1, 1), now);
        balance.RecordUsage(10m, now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new DateOnly(2020, 1, 1));

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, leavingDate, now),
            CancellationToken.None);

        var updated = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0.68m, updated.EntitlementDays); // 25 * 10 / 365 = 0.6849... rounded to 0.68
        Assert.Equal(10m, updated.UsedDays);
        Assert.Equal(-9.32m, updated.RemainingDays); // 0.68 + 0 - 10
    }

    [Fact]
    public async Task HandleAsync_Never_Recalculates_Toil_Balances()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var leavingDate = new DateOnly(2026, 6, 30);
        var policyYear = LeaveYearCalculator.GetPolicyYear(leavingDate, startMonth: 1);

        var toilType = LeaveType.Create(Guid.NewGuid(), companyId, "Time Off In Lieu", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, now);
        context.LeaveTypes.Add(toilType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, toilType.Id,
            Guid.NewGuid(), policyYear, 5m, new DateOnly(2026, 1, 1), now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new DateOnly(2020, 1, 1));

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, leavingDate, now),
            CancellationToken.None);

        var unchanged = await context.LeaveBalances.SingleAsync();
        Assert.Equal(5m, unchanged.EntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_Does_Nothing_When_Employee_StartDate_Cannot_Be_Resolved()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var leavingDate = new DateOnly(2026, 6, 30);
        var policyYear = LeaveYearCalculator.GetPolicyYear(leavingDate, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 1, 1), now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, startDate: null);

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, leavingDate, now),
            CancellationToken.None);

        var unchanged = await context.LeaveBalances.SingleAsync();
        Assert.Equal(25m, unchanged.EntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_Does_Nothing_When_No_Balances_Exist_For_Target_Policy_Year()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var leavingDate = new DateOnly(2026, 6, 30);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new DateOnly(2020, 1, 1));

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, leavingDate, now),
            CancellationToken.None);

        Assert.Empty(context.LeaveBalances);
    }

    [Fact]
    public async Task HandleAsync_LeavingDateSet_Recalculates_The_Policy_Year_Containing_A_FutureDated_LeavingDate()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero); // "today" is 2026-06-11
        var futureLeavingDate = new DateOnly(2027, 6, 30); // next policy year
        var currentPolicyYear = LeaveYearCalculator.GetPolicyYear(now, startMonth: 1);
        var futurePolicyYear = LeaveYearCalculator.GetPolicyYear(futureLeavingDate, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var currentYearBalance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), currentPolicyYear, 25m, new DateOnly(2026, 1, 1), now);
        var futureYearBalance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), futurePolicyYear, 25m, new DateOnly(2027, 1, 1), now);
        context.LeaveBalances.AddRange(currentYearBalance, futureYearBalance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new DateOnly(2020, 1, 1));

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, futureLeavingDate, now),
            CancellationToken.None);

        var untouchedCurrentYear = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == currentPolicyYear);
        var recalculatedFutureYear = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == futurePolicyYear);

        Assert.Equal(25m, untouchedCurrentYear.EntitlementDays);
        Assert.Equal(12.4m, recalculatedFutureYear.EntitlementDays); // 25 * 181 / 365 = 12.397... rounded to 12.4
    }

    [Fact]
    public async Task HandleAsync_LeavingDateSet_Is_Idempotent_When_Delivered_Twice_With_Same_Event()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var leavingDate = new DateOnly(2026, 6, 30);
        var policyYear = LeaveYearCalculator.GetPolicyYear(leavingDate, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 1, 1), now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new DateOnly(2020, 1, 1));
        var integrationEvent = new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, leavingDate, now);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        var afterFirst = await context.LeaveBalances.SingleAsync();
        var afterFirstEntitlement = afterFirst.EntitlementDays;

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        var afterSecond = await context.LeaveBalances.SingleAsync();

        Assert.Equal(12.4m, afterFirstEntitlement);
        Assert.Equal(afterFirstEntitlement, afterSecond.EntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_LeavingDateSet_ProRates_Correctly_For_NonCalendar_LeaveYear()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        // April-to-March leave year. Leaves 2026-10-31.
        var leavingDate = new DateOnly(2026, 10, 31);
        var leaveSettings = new HR.Infrastructure.Abstractions.CompanyLeaveSettings(
            ExcludePublicHolidaysFromLeave: true,
            LeaveYearStartMonth: 4,
            DefaultHolidayAllowance: 25,
            WorkingPattern: HR.Modules.Employees.Contracts.WorkingPattern.Default);
        var policyYear = LeaveYearCalculator.GetPolicyYear(leavingDate, startMonth: 4);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, new DateOnly(2026, 4, 1), now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new LeavingDateChangeHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(leaveSettings),
            new FakeEmployeeStartDateReader(new DateOnly(2020, 1, 1)));

        await handler.HandleAsync(
            new EmployeeLeavingDateSetIntegrationEvent(companyId, employeeId, leavingDate, now),
            CancellationToken.None);

        var updated = await context.LeaveBalances.SingleAsync();
        Assert.Equal(14.66m, updated.EntitlementDays); // 25 * 214 / 365 = 14.657... rounded to 14.66
    }

    private static LeavingDateChangeHandler BuildHandler(LeaveDbContext context, DateOnly? startDate) =>
        new(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(startDate));

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
