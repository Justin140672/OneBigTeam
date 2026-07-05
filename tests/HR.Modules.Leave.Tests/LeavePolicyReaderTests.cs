using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class LeavePolicyReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExistsAsync_Returns_True_For_Active_Policy_In_Same_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 5, false, Now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var reader = new LeavePolicyReader(context);

        Assert.True(await reader.ExistsAsync(companyId, policy.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ExistsAsync_Returns_False_When_Policy_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var reader = new LeavePolicyReader(context);

        Assert.False(await reader.ExistsAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ExistsAsync_Returns_False_When_Policy_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var policy = LeavePolicy.Create(Guid.NewGuid(), Guid.NewGuid(), "Standard", null, 5, false, Now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var reader = new LeavePolicyReader(context);

        Assert.False(await reader.ExistsAsync(Guid.NewGuid(), policy.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ExistsAsync_Returns_False_When_Policy_Is_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 5, false, Now);
        policy.Deactivate(Now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var reader = new LeavePolicyReader(context);

        Assert.False(await reader.ExistsAsync(companyId, policy.Id, CancellationToken.None));
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
