using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.RecalculateEntitlementOnStartDateChange;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class EmployeeDetailsCorrectedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 11, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Recalculates_Entitlement_When_Balance_Has_No_Usage_Or_Adjustment()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var policyYear = LeaveYearCalculator.GetPolicyYear(now, startMonth: 1);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);

        // Originally created assuming a Jan 1 start (full entitlement).
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id,
            Guid.NewGuid(), policyYear, 25m, now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        // Start date corrected to mid-year.
        var handler = new EmployeeDetailsCorrectedHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(new DateOnly(2026, 6, 1)));

        await handler.HandleAsync(
            new EmployeeDetailsCorrectedIntegrationEvent(companyId, employeeId, now),
            CancellationToken.None);

        var updated = await context.LeaveBalances.SingleAsync();
        Assert.Equal(14.66m, updated.EntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Overwrite_Balance_That_Has_Recorded_Usage()
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
            Guid.NewGuid(), policyYear, 25m, now);
        balance.RecordUsage(3m, now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new EmployeeDetailsCorrectedHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(new DateOnly(2026, 6, 1)));

        await handler.HandleAsync(
            new EmployeeDetailsCorrectedIntegrationEvent(companyId, employeeId, now),
            CancellationToken.None);

        var unchanged = await context.LeaveBalances.SingleAsync();
        Assert.Equal(25m, unchanged.EntitlementDays);
        Assert.Equal(3m, unchanged.UsedDays);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Overwrite_Balance_That_Has_A_Manual_Adjustment()
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
            Guid.NewGuid(), policyYear, 25m, now);
        balance.Adjust(2m, now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new EmployeeDetailsCorrectedHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(new DateOnly(2026, 6, 1)));

        await handler.HandleAsync(
            new EmployeeDetailsCorrectedIntegrationEvent(companyId, employeeId, now),
            CancellationToken.None);

        var unchanged = await context.LeaveBalances.SingleAsync();
        Assert.Equal(25m, unchanged.EntitlementDays);
        Assert.Equal(2m, unchanged.AdjustmentDays);
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
            Guid.NewGuid(), policyYear, 25m, now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new EmployeeDetailsCorrectedHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(null));

        await handler.HandleAsync(
            new EmployeeDetailsCorrectedIntegrationEvent(companyId, employeeId, now),
            CancellationToken.None);

        var unchanged = await context.LeaveBalances.SingleAsync();
        Assert.Equal(25m, unchanged.EntitlementDays);
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
            Guid.NewGuid(), policyYear, 0m, now);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new EmployeeDetailsCorrectedHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeStartDateReader(new DateOnly(2026, 6, 1)));

        await handler.HandleAsync(
            new EmployeeDetailsCorrectedIntegrationEvent(companyId, employeeId, now),
            CancellationToken.None);

        var unchanged = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, unchanged.EntitlementDays);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
