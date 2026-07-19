using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.UpdateLeavePolicy;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class UpdateLeavePolicyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Updates_Policy_And_Returns_Response()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Old Name", "Old desc", 3, false, false, now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var handler = new UpdateLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateLeavePolicyRequest
            {
                CompanyId = companyId,
                PolicyId = policy.Id,
                Name = "New Name",
                Description = "New desc",
                CarryOverDays = 10,
                AllowNegativeBalance = true
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", result.Value!.Name);
        Assert.Equal("New desc", result.Value.Description);
        Assert.Equal(10, result.Value.CarryOverDays);
        Assert.True(result.Value.AllowNegativeBalance);
        Assert.Equal(now, result.Value.UpdatedAt);

        var saved = await context.LeavePolicies.SingleAsync();
        Assert.Equal("New Name", saved.Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Policy_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdateLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateLeavePolicyRequest
            {
                CompanyId = Guid.NewGuid(),
                PolicyId = Guid.NewGuid(),
                Name = "Any Name"
            },
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

        var policy = LeavePolicy.Create(Guid.NewGuid(), companyA, "Standard", null, 0, false, false, now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var handler = new UpdateLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateLeavePolicyRequest
            {
                CompanyId = companyB,
                PolicyId = policy.Id,
                Name = "Standard"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Name_Taken_By_Different_Policy()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var existing = LeavePolicy.Create(Guid.NewGuid(), companyId, "Taken Name", null, 0, false, false, now);
        var target = LeavePolicy.Create(Guid.NewGuid(), companyId, "Original Name", null, 0, false, false, now);
        context.LeavePolicies.AddRange(existing, target);
        await context.SaveChangesAsync();

        var handler = new UpdateLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateLeavePolicyRequest
            {
                CompanyId = companyId,
                PolicyId = target.Id,
                Name = "Taken Name"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Keeping_Same_Name()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Same Name", null, 5, false, false, now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var handler = new UpdateLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateLeavePolicyRequest
            {
                CompanyId = companyId,
                PolicyId = policy.Id,
                Name = "Same Name",
                CarryOverDays = 10
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value!.CarryOverDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_When_Removing_Default_From_Only_Default_Policy()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 0, false, true, now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var handler = new UpdateLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateLeavePolicyRequest
            {
                CompanyId = companyId,
                PolicyId = policy.Id,
                Name = "Standard",
                IsDefault = false
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(
            "At least one Leave Policy must be marked as default — set a different policy as default instead of removing this one.",
            result.Error.Message);

        var saved = await context.LeavePolicies.SingleAsync();
        Assert.True(saved.IsDefault);
    }

    [Fact]
    public async Task HandleAsync_Updating_Other_Fields_On_NonDefault_Policy_Without_Touching_IsDefault_Succeeds()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Old Name", null, 0, false, false, now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var handler = new UpdateLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateLeavePolicyRequest
            {
                CompanyId = companyId,
                PolicyId = policy.Id,
                Name = "New Name",
                CarryOverDays = 7,
                IsDefault = false
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", result.Value!.Name);
        Assert.Equal(7, result.Value.CarryOverDays);
        Assert.False(result.Value.IsDefault);
    }

    [Fact]
    public async Task HandleAsync_Setting_IsDefault_True_On_NonDefault_Policy_Swaps_Default()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var currentDefault = LeavePolicy.Create(Guid.NewGuid(), companyId, "Current Default", null, 0, false, true, now);
        var target = LeavePolicy.Create(Guid.NewGuid(), companyId, "Target", null, 0, false, false, now);
        context.LeavePolicies.AddRange(currentDefault, target);
        await context.SaveChangesAsync();

        var handler = new UpdateLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateLeavePolicyRequest
            {
                CompanyId = companyId,
                PolicyId = target.Id,
                Name = "Target",
                IsDefault = true
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDefault);

        var reloadedCurrentDefault = await context.LeavePolicies.SingleAsync(p => p.Id == currentDefault.Id);
        Assert.False(reloadedCurrentDefault.IsDefault);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
