using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.GetEmployeeLeaveBalance;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class GetEmployeeLeaveBalanceHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Returns_All_Balances_For_Employee_And_Year()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var annualTypeId = Guid.NewGuid();
        var sickTypeId = Guid.NewGuid();

        context.LeaveTypes.AddRange(
            LeaveType.Create(annualTypeId, companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now),
            LeaveType.Create(sickTypeId, companyId, "Sick Leave", "SICK", 10, AccrualMethod.None, LeaveTypeBehaviour.Sickness, now));

        var annualBalance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, annualTypeId, policyId, 2026, 25m, now);
        var sickBalance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, sickTypeId, policyId, 2026, 10m, now);
        context.LeaveBalances.AddRange(annualBalance, sickBalance);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeLeaveBalanceHandler(context, new FakeWorkingPatternProvider());

        var result = await handler.HandleAsync(
            new GetEmployeeLeaveBalanceRequest { CompanyId = companyId, EmployeeId = employeeId, PolicyYear = 2026 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(employeeId, result.Value!.EmployeeId);
        Assert.Equal(2026, result.Value.PolicyYear);
        Assert.Equal(2, result.Value.Balances.Count);

        // Ordered by LeaveTypeName: Annual Leave < Sick Leave
        var annual = result.Value.Balances[0];
        Assert.True(annual.HasBalance);
        Assert.Equal("Annual Leave", annual.LeaveTypeName);
        Assert.Equal("ANNUAL", annual.LeaveTypeCode);
        Assert.Equal(25m, annual.EntitlementDays);
        Assert.Equal(0m, annual.UsedDays);
        Assert.Equal(0m, annual.AdjustmentDays);
        Assert.Equal(25m, annual.RemainingDays); // 25 + 0 - 0
        Assert.Equal(25m * WorkingPattern.Default.HoursPerDay, annual.EntitlementHours);
        Assert.Equal(25m * WorkingPattern.Default.HoursPerDay, annual.RemainingHours);

        var sick = result.Value.Balances[1];
        Assert.True(sick.HasBalance);
        Assert.Equal("Sick Leave", sick.LeaveTypeName);
        Assert.Equal("SICK", sick.LeaveTypeCode);
        Assert.Equal(10m, sick.EntitlementDays);
        Assert.Equal(0m, sick.UsedDays);
        Assert.Equal(0m, sick.AdjustmentDays);
        Assert.Equal(10m, sick.RemainingDays); // 10 + 0 - 0
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Active_Leave_Types_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetEmployeeLeaveBalanceHandler(context, new FakeWorkingPatternProvider());

        var result = await handler.HandleAsync(
            new GetEmployeeLeaveBalanceRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), PolicyYear = 2026 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Balances);
    }

    [Fact]
    public async Task HandleAsync_Returns_NoBalance_Row_When_Employee_Has_No_Balance_For_Type()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var targetEmployee = Guid.NewGuid();
        var otherEmployee = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var typeId = Guid.NewGuid();
        context.LeaveTypes.Add(
            LeaveType.Create(typeId, companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now));

        context.LeaveBalances.Add(
            LeaveBalance.Create(Guid.NewGuid(), companyId, otherEmployee, typeId, policyId, 2026, 25m, now));
        await context.SaveChangesAsync();

        var handler = new GetEmployeeLeaveBalanceHandler(context, new FakeWorkingPatternProvider());

        var result = await handler.HandleAsync(
            new GetEmployeeLeaveBalanceRequest { CompanyId = companyId, EmployeeId = targetEmployee, PolicyYear = 2026 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Balances);
        Assert.False(item.HasBalance);
        Assert.Null(item.LeaveBalanceId);
        Assert.Null(item.EntitlementDays);
        Assert.Null(item.RemainingDays);
        Assert.Null(item.EntitlementHours);
        Assert.Null(item.RemainingHours);
    }

    [Fact]
    public async Task HandleAsync_Returns_NoBalance_Row_When_Balance_Exists_For_Different_Year()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var typeId = Guid.NewGuid();
        context.LeaveTypes.Add(
            LeaveType.Create(typeId, companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now));

        context.LeaveBalances.Add(
            LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, typeId, policyId, 2025, 25m, now));
        await context.SaveChangesAsync();

        var handler = new GetEmployeeLeaveBalanceHandler(context, new FakeWorkingPatternProvider());

        var result = await handler.HandleAsync(
            new GetEmployeeLeaveBalanceRequest { CompanyId = companyId, EmployeeId = employeeId, PolicyYear = 2026 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Balances);
        Assert.False(item.HasBalance);
    }

    [Fact]
    public async Task HandleAsync_Returns_Correct_Balance_When_Adjustment_And_Usage_Applied()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var typeId = Guid.NewGuid();
        context.LeaveTypes.Add(
            LeaveType.Create(typeId, companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now));

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, typeId, policyId, 2026, 25m, now);
        balance.Adjust(2m, now);       // +2 carry-over adjustment
        balance.RecordUsage(5m, now);  // 5 days used
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new GetEmployeeLeaveBalanceHandler(context, new FakeWorkingPatternProvider());
        var result = await handler.HandleAsync(
            new GetEmployeeLeaveBalanceRequest { CompanyId = companyId, EmployeeId = employeeId, PolicyYear = 2026 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Balances);
        Assert.True(item.HasBalance);
        Assert.Equal(25m, item.EntitlementDays);
        Assert.Equal(2m, item.AdjustmentDays);
        Assert.Equal(5m, item.UsedDays);
        Assert.Equal(22m, item.RemainingDays); // 25 + 2 - 5
        Assert.Equal(22m * WorkingPattern.Default.HoursPerDay, item.RemainingHours);
    }

    [Fact]
    public async Task HandleAsync_Converts_Balance_To_Hours_Using_Effective_Working_Pattern()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var typeId = Guid.NewGuid();
        context.LeaveTypes.Add(
            LeaveType.Create(typeId, companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now));

        context.LeaveBalances.Add(
            LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, typeId, policyId, 2026, 20m, now));
        await context.SaveChangesAsync();

        var customPattern = new WorkingPattern(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday, 8m);
        var handler = new GetEmployeeLeaveBalanceHandler(context, new FakeWorkingPatternProvider(customPattern));

        var result = await handler.HandleAsync(
            new GetEmployeeLeaveBalanceRequest { CompanyId = companyId, EmployeeId = employeeId, PolicyYear = 2026 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Balances);
        Assert.Equal(160m, item.EntitlementHours); // 20 days * 8 hours/day
        Assert.Equal(160m, item.RemainingHours);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
