using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class LeavePolicyProvisionerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EnsureDefaultLeavePolicyAsync_Creates_Standard_Default_Policy_When_None_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var provisioner = new LeavePolicyProvisioner(context, new FakeClock(FixedUtcNow));

        var policyId = await provisioner.EnsureDefaultLeavePolicyAsync(companyId, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, policyId);

        var saved = await context.LeavePolicies.SingleAsync(p => p.CompanyId == companyId);
        Assert.Equal(policyId, saved.Id);
        Assert.Equal("Standard", saved.Name);
        Assert.True(saved.IsDefault);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task EnsureDefaultLeavePolicyAsync_Returns_Existing_Default_Without_Creating_Second_One()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var existing = LeavePolicy.Create(
            Guid.NewGuid(), companyId, "Existing Default", null, carryOverDays: 3, allowNegativeBalance: false, isDefault: true, now);
        context.LeavePolicies.Add(existing);
        await context.SaveChangesAsync();

        var provisioner = new LeavePolicyProvisioner(context, new FakeClock(FixedUtcNow));

        var policyId = await provisioner.EnsureDefaultLeavePolicyAsync(companyId, CancellationToken.None);

        Assert.Equal(existing.Id, policyId);

        var count = await context.LeavePolicies.CountAsync(p => p.CompanyId == companyId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task EnsureDefaultLeavePolicyAsync_Ignores_Inactive_Default_Policy_And_Creates_New_One()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var inactiveDefault = LeavePolicy.Create(
            Guid.NewGuid(), companyId, "Old Default", null, carryOverDays: 3, allowNegativeBalance: false, isDefault: true, now);
        inactiveDefault.Deactivate(now);
        context.LeavePolicies.Add(inactiveDefault);
        await context.SaveChangesAsync();

        var provisioner = new LeavePolicyProvisioner(context, new FakeClock(FixedUtcNow));

        var policyId = await provisioner.EnsureDefaultLeavePolicyAsync(companyId, CancellationToken.None);

        Assert.NotEqual(inactiveDefault.Id, policyId);

        var saved = await context.LeavePolicies.SingleAsync(p => p.Id == policyId);
        Assert.Equal("Standard", saved.Name);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task EnsureDefaultLeavePolicyAsync_Scopes_Default_Policy_Lookup_By_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var companyADefault = LeavePolicy.Create(
            Guid.NewGuid(), companyA, "Company A Default", null, carryOverDays: 3, allowNegativeBalance: false, isDefault: true, now);
        context.LeavePolicies.Add(companyADefault);
        await context.SaveChangesAsync();

        var provisioner = new LeavePolicyProvisioner(context, new FakeClock(FixedUtcNow));

        var policyId = await provisioner.EnsureDefaultLeavePolicyAsync(companyB, CancellationToken.None);

        Assert.NotEqual(companyADefault.Id, policyId);
        var saved = await context.LeavePolicies.SingleAsync(p => p.Id == policyId);
        Assert.Equal(companyB, saved.CompanyId);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
