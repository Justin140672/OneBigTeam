using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class AssignLeavePolicyToEmployeeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly EffectiveDate = new(2026, 7, 1);

    [Fact]
    public async Task HandleAsync_Creates_Assignment_When_None_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 5, false, now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var handler = new AssignLeavePolicyToEmployeeHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());

        var result = await handler.HandleAsync(
            new AssignLeavePolicyToEmployeeRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeavePolicyId = policy.Id,
                EffectiveFrom = EffectiveDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal(policy.Id, result.Value.LeavePolicyId);
        Assert.Equal(EffectiveDate, result.Value.EffectiveFrom);
        Assert.Equal(now, result.Value.CreatedAt);

        var saved = await context.EmployeeLeavePolicyAssignments.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Updates_Assignment_When_One_Already_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var oldPolicy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Old Policy", null, 0, false, now);
        var newPolicy = LeavePolicy.Create(Guid.NewGuid(), companyId, "New Policy", null, 10, false, now);
        context.LeavePolicies.AddRange(oldPolicy, newPolicy);

        var existingAssignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, oldPolicy.Id, new DateOnly(2026, 1, 1), now);
        context.EmployeeLeavePolicyAssignments.Add(existingAssignment);
        await context.SaveChangesAsync();

        var handler = new AssignLeavePolicyToEmployeeHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());

        var result = await handler.HandleAsync(
            new AssignLeavePolicyToEmployeeRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeavePolicyId = newPolicy.Id,
                EffectiveFrom = EffectiveDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existingAssignment.Id, result.Value!.Id);
        Assert.Equal(newPolicy.Id, result.Value.LeavePolicyId);
        Assert.Equal(EffectiveDate, result.Value.EffectiveFrom);

        var saved = await context.EmployeeLeavePolicyAssignments.SingleAsync();
        Assert.Equal(newPolicy.Id, saved.LeavePolicyId);
        Assert.Equal(EffectiveDate, saved.EffectiveFrom);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Policy_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new AssignLeavePolicyToEmployeeHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());

        var result = await handler.HandleAsync(
            new AssignLeavePolicyToEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                EmployeeId = Guid.NewGuid(),
                LeavePolicyId = Guid.NewGuid(),
                EffectiveFrom = EffectiveDate
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Policy_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var policy = LeavePolicy.Create(Guid.NewGuid(), Guid.NewGuid(), "Standard", null, 5, false, now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var handler = new AssignLeavePolicyToEmployeeHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());

        var result = await handler.HandleAsync(
            new AssignLeavePolicyToEmployeeRequest
            {
                CompanyId = Guid.NewGuid(), // different company
                EmployeeId = Guid.NewGuid(),
                LeavePolicyId = policy.Id,
                EffectiveFrom = EffectiveDate
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
