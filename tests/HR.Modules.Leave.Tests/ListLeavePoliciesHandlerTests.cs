using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ListLeavePolicies;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class ListLeavePoliciesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_All_Policies_For_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.LeavePolicies.AddRange(
            LeavePolicy.Create(Guid.NewGuid(), companyId, "Policy B", null, 0, false, Now),
            LeavePolicy.Create(Guid.NewGuid(), companyId, "Policy A", null, 5, true, Now));
        await context.SaveChangesAsync();

        var handler = new ListLeavePoliciesHandler(context);
        var result = await handler.HandleAsync(
            new ListLeavePoliciesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Policy A", result.Items[0].Name);
        Assert.Equal("Policy B", result.Items[1].Name);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Policies_From_Other_Companies()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        context.LeavePolicies.AddRange(
            LeavePolicy.Create(Guid.NewGuid(), companyA, "Policy A", null, 0, false, Now),
            LeavePolicy.Create(Guid.NewGuid(), companyB, "Policy B", null, 0, false, Now));
        await context.SaveChangesAsync();

        var handler = new ListLeavePoliciesHandler(context);
        var result = await handler.HandleAsync(
            new ListLeavePoliciesRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Policy A", result.Items[0].Name);
    }

    [Fact]
    public async Task HandleAsync_Filters_To_Active_Only_When_Requested()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var active = LeavePolicy.Create(Guid.NewGuid(), companyId, "Active", null, 0, false, Now);
        var inactive = LeavePolicy.Create(Guid.NewGuid(), companyId, "Inactive", null, 0, false, Now);
        inactive.Deactivate(Now);

        context.LeavePolicies.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var handler = new ListLeavePoliciesHandler(context);
        var result = await handler.HandleAsync(
            new ListLeavePoliciesRequest { CompanyId = companyId, ActiveOnly = true },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Active", result.Items[0].Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_Both_Active_And_Inactive_When_ActiveOnly_Is_False()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var active = LeavePolicy.Create(Guid.NewGuid(), companyId, "Active", null, 0, false, Now);
        var inactive = LeavePolicy.Create(Guid.NewGuid(), companyId, "Inactive", null, 0, false, Now);
        inactive.Deactivate(Now);

        context.LeavePolicies.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var handler = new ListLeavePoliciesHandler(context);
        var result = await handler.HandleAsync(
            new ListLeavePoliciesRequest { CompanyId = companyId, ActiveOnly = false },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Policies_Exist()
    {
        await using var context = BuildContext();
        var handler = new ListLeavePoliciesHandler(context);

        var result = await handler.HandleAsync(
            new ListLeavePoliciesRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
