using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.InitialiseEmployeeLeave;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class EmployeeCreatedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 11, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_Balances_For_All_Active_LeaveTypes()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var activeType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        var inactiveType = LeaveType.Create(Guid.NewGuid(), companyId, "Sick Leave", "SICK", 10,
            AccrualMethod.None, LeaveTypeBehaviour.Sickness, now);
        inactiveType.Deactivate(now);

        context.LeaveTypes.AddRange(activeType, inactiveType);
        context.EmployeeLeavePolicyAssignments.Add(
            EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policyId,
                DateOnly.FromDateTime(FixedUtcNow), now));
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow));
        await handler.HandleAsync(new EmployeeCreatedIntegrationEvent(companyId, employeeId), CancellationToken.None);

        var balances = await context.LeaveBalances.ToListAsync();
        Assert.Single(balances);
        Assert.Equal(employeeId, balances[0].EmployeeId);
        Assert.Equal(activeType.Id, balances[0].LeaveTypeId);
        Assert.Equal(policyId, balances[0].LeavePolicyId);
        Assert.Equal(25, balances[0].EntitlementDays);
        Assert.Equal(FixedUtcNow.Year, balances[0].PolicyYear);
    }

    [Fact]
    public async Task HandleAsync_Does_Nothing_When_No_Active_LeaveTypes()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var inactiveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        inactiveType.Deactivate(now);
        context.LeaveTypes.Add(inactiveType);
        context.EmployeeLeavePolicyAssignments.Add(
            EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policyId,
                DateOnly.FromDateTime(FixedUtcNow), now));
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow));
        await handler.HandleAsync(new EmployeeCreatedIntegrationEvent(companyId, employeeId), CancellationToken.None);

        Assert.Empty(await context.LeaveBalances.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Does_Nothing_When_Employee_Has_No_Policy_Assignment()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var activeType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(activeType);
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow));
        await handler.HandleAsync(new EmployeeCreatedIntegrationEvent(companyId, employeeId), CancellationToken.None);

        Assert.Empty(await context.LeaveBalances.ToListAsync());
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
