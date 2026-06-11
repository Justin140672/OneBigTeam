using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.CreateLeavePolicy;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class CreateLeavePolicyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_LeavePolicy_And_Returns_Response()
    {
        await using var context = BuildContext();
        var handler = new CreateLeavePolicyHandler(context, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreateLeavePolicyRequest
            {
                CompanyId = companyId,
                Name = "Standard Policy",
                Description = "Default leave policy",
                CarryOverDays = 5,
                AllowNegativeBalance = false
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal("Standard Policy", result.Value.Name);
        Assert.Equal("Default leave policy", result.Value.Description);
        Assert.Equal(5, result.Value.CarryOverDays);
        Assert.False(result.Value.AllowNegativeBalance);
        Assert.True(result.Value.IsActive);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value.CreatedAt);

        var saved = await context.LeavePolicies.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Name_Already_Exists_In_Same_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.LeavePolicies.Add(
            LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 0, false, now));
        await context.SaveChangesAsync();

        var handler = new CreateLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateLeavePolicyRequest { CompanyId = companyId, Name = "Standard Policy" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Name_In_Different_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.LeavePolicies.Add(
            LeavePolicy.Create(Guid.NewGuid(), companyA, "Standard Policy", null, 0, false, now));
        await context.SaveChangesAsync();

        var handler = new CreateLeavePolicyHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateLeavePolicyRequest { CompanyId = companyB, Name = "Standard Policy" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
