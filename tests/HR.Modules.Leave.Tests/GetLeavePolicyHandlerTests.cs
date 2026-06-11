using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.GetLeavePolicy;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class GetLeavePolicyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Returns_Policy_When_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", "Default leave policy", 5, false, now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var handler = new GetLeavePolicyHandler(context);

        var result = await handler.HandleAsync(
            new GetLeavePolicyRequest { CompanyId = companyId, Id = policy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(policy.Id, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal("Standard Policy", result.Value.Name);
        Assert.Equal("Default leave policy", result.Value.Description);
        Assert.Equal(5, result.Value.CarryOverDays);
        Assert.False(result.Value.AllowNegativeBalance);
        Assert.True(result.Value.IsActive);
        Assert.Equal(now, result.Value.CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Policy_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetLeavePolicyHandler(context);

        var result = await handler.HandleAsync(
            new GetLeavePolicyRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Policy_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var policy = LeavePolicy.Create(Guid.NewGuid(), Guid.NewGuid(), "Standard Policy", null, 0, false, now);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();

        var handler = new GetLeavePolicyHandler(context);

        var result = await handler.HandleAsync(
            new GetLeavePolicyRequest { CompanyId = Guid.NewGuid(), Id = policy.Id },
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
