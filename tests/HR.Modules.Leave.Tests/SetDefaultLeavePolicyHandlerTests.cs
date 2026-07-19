using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.SetDefaultLeavePolicy;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class SetDefaultLeavePolicyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Swaps_Default_From_PolicyA_To_PolicyB()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var policyA = LeavePolicy.Create(Guid.NewGuid(), companyId, "Policy A", null, 0, false, true, now);
        var policyB = LeavePolicy.Create(Guid.NewGuid(), companyId, "Policy B", null, 0, false, false, now);
        context.LeavePolicies.AddRange(policyA, policyB);
        await context.SaveChangesAsync();

        var handler = new SetDefaultLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new SetDefaultLeavePolicyRequest { CompanyId = companyId, Id = policyB.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var reloadedA = await context.LeavePolicies.SingleAsync(p => p.Id == policyA.Id);
        var reloadedB = await context.LeavePolicies.SingleAsync(p => p.Id == policyB.Id);
        Assert.False(reloadedA.IsDefault);
        Assert.True(reloadedB.IsDefault);
    }

    [Fact]
    public async Task HandleAsync_Is_NoOp_Success_When_Policy_Is_Already_Default()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Policy", null, 0, false, true, now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var handler = new SetDefaultLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new SetDefaultLeavePolicyRequest { CompanyId = companyId, Id = policy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var reloaded = await context.LeavePolicies.SingleAsync(p => p.Id == policy.Id);
        Assert.True(reloaded.IsDefault);
        Assert.Equal(now, reloaded.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_When_Policy_Is_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Policy", null, 0, false, false, now);
        policy.Deactivate(now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var handler = new SetDefaultLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new SetDefaultLeavePolicyRequest { CompanyId = companyId, Id = policy.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal("Cannot set an inactive leave policy as the default.", result.Error.Message);

        var reloaded = await context.LeavePolicies.SingleAsync(p => p.Id == policy.Id);
        Assert.False(reloaded.IsDefault);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Policy_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new SetDefaultLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new SetDefaultLeavePolicyRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Policy_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var policy = LeavePolicy.Create(Guid.NewGuid(), companyA, "Policy", null, 0, false, false, now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var handler = new SetDefaultLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new SetDefaultLeavePolicyRequest { CompanyId = companyB, Id = policy.Id },
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
