using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services.OnboardingTasks;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class ReviewDefaultLeavePolicyTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IsCompletedAsync_Returns_False_For_Never_Updated_Default_Policy()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Default", null, 5, false, isDefault: true, Now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var task = new ReviewDefaultLeavePolicyTask(context);

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_True_Once_Default_Policy_Updated()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Default", null, 5, false, isDefault: true, Now);
        policy.Update("Default Policy", "Updated description", 5, false, Now.AddDays(1));
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var task = new ReviewDefaultLeavePolicyTask(context);

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_No_Default_Policy_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Non-default", null, 5, false, isDefault: false, Now);
        policy.Update("Non-default", "Updated description", 5, false, Now.AddDays(1));
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var task = new ReviewDefaultLeavePolicyTask(context);

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.False(result);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }
}
