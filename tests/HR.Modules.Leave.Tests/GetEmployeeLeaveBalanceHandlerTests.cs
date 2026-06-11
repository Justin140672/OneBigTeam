using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.GetEmployeeLeaveBalance;
using HR.Modules.Leave.Persistence;
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

        var handler = new GetEmployeeLeaveBalanceHandler(context);

        var result = await handler.HandleAsync(
            new GetEmployeeLeaveBalanceRequest { CompanyId = companyId, EmployeeId = employeeId, PolicyYear = 2026 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(employeeId, result.Value!.EmployeeId);
        Assert.Equal(2026, result.Value.PolicyYear);
        Assert.Equal(2, result.Value.Balances.Count);

        // Ordered by LeaveTypeName: Annual Leave < Sick Leave
        var annual = result.Value.Balances[0];
        Assert.Equal("Annual Leave", annual.LeaveTypeName);
        Assert.Equal("ANNUAL", annual.LeaveTypeCode);
        Assert.Equal(25m, annual.EntitlementDays);
        Assert.Equal(0m, annual.UsedDays);
        Assert.Equal(0m, annual.AdjustmentDays);
        Assert.Equal(25m, annual.RemainingDays); // 25 + 0 - 0

        var sick = result.Value.Balances[1];
        Assert.Equal("Sick Leave", sick.LeaveTypeName);
        Assert.Equal("SICK", sick.LeaveTypeCode);
        Assert.Equal(10m, sick.EntitlementDays);
        Assert.Equal(0m, sick.UsedDays);
        Assert.Equal(0m, sick.AdjustmentDays);
        Assert.Equal(10m, sick.RemainingDays); // 10 + 0 - 0
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Balances_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetEmployeeLeaveBalanceHandler(context);

        var result = await handler.HandleAsync(
            new GetEmployeeLeaveBalanceRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), PolicyYear = 2026 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Balances);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Balances_For_Different_Employee()
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

        var handler = new GetEmployeeLeaveBalanceHandler(context);

        var result = await handler.HandleAsync(
            new GetEmployeeLeaveBalanceRequest { CompanyId = companyId, EmployeeId = targetEmployee, PolicyYear = 2026 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Balances);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Balances_For_Different_Year()
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

        var handler = new GetEmployeeLeaveBalanceHandler(context);

        var result = await handler.HandleAsync(
            new GetEmployeeLeaveBalanceRequest { CompanyId = companyId, EmployeeId = employeeId, PolicyYear = 2026 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Balances);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
