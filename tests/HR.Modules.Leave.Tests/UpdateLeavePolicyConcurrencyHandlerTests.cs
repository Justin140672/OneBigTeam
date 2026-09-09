using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.UpdateLeavePolicy;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

// Ticket 2 (optimistic concurrency rollout): LeavePolicy.Version coverage for UpdateLeavePolicy.
// Two DbContext instances over the same EF InMemory database; context B saves first (bumping the
// store's Version), then the handler under test saves against context A with a stale
// ExpectedVersion, raising DbUpdateConcurrencyException exactly as real Postgres would.
public class UpdateLeavePolicyConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<LeaveDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<LeaveDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task<(string DbName, Guid CompanyId, Guid PolicyId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Original", "desc", 3, false, false, now);

        await using var seed = new LeaveDbContext(Options(dbName));
        seed.LeavePolicies.Add(policy);
        await seed.SaveChangesAsync();

        return (dbName, companyId, policy.Id);
    }

    private static UpdateLeavePolicyRequest Request(Guid companyId, Guid policyId, int? expectedVersion, string name = "Original", int carryOver = 3)
        => new()
        {
            CompanyId = companyId,
            PolicyId = policyId,
            Name = name,
            CarryOverDays = carryOver,
            ExpectedVersion = expectedVersion,
        };

    [Fact]
    public void IncrementVersion_Increments_Version_By_One()
    {
        var policy = LeavePolicy.Create(Guid.NewGuid(), Guid.NewGuid(), "P", null, 0, false, false, new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        Assert.Equal(1, policy.Version);

        policy.IncrementVersion();
        Assert.Equal(2, policy.Version);
    }

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, policyId) = await SeedAsync();

        await using var ctx = new LeaveDbContext(Options(dbName));
        var result = await new UpdateLeavePolicyHandler(ctx, new FakeClock(FixedUtcNow), new NoOpAuditEventPublisher())
            .HandleAsync(Request(companyId, policyId, expectedVersion: 1, carryOver: 9), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
        Assert.Equal(9, result.Value.CarryOverDays);

        await using var verify = new LeaveDbContext(Options(dbName));
        Assert.Equal(2, (await verify.LeavePolicies.SingleAsync()).Version);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, policyId) = await SeedAsync();

        await using var ctx = new LeaveDbContext(Options(dbName));
        var result = await new UpdateLeavePolicyHandler(ctx, new FakeClock(FixedUtcNow), new NoOpAuditEventPublisher())
            .HandleAsync(Request(companyId, policyId, expectedVersion: null, carryOver: 7), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new LeaveDbContext(Options(dbName));
        var saved = await verify.LeavePolicies.SingleAsync();
        Assert.Equal(3, saved.CarryOverDays);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Publishes_No_Audit_Event()
    {
        var (dbName, companyId, policyId) = await SeedAsync();

        await using var ctxA = new LeaveDbContext(Options(dbName));
        await ctxA.LeavePolicies.SingleAsync();

        await using (var ctxB = new LeaveDbContext(Options(dbName)))
        {
            var winner = await new UpdateLeavePolicyHandler(ctxB, new FakeClock(FixedUtcNow), new NoOpAuditEventPublisher())
                .HandleAsync(Request(companyId, policyId, expectedVersion: 1, carryOver: 11), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new CapturingAuditEventPublisher();
        var result = await new UpdateLeavePolicyHandler(ctxA, new FakeClock(FixedUtcNow), audit)
            .HandleAsync(Request(companyId, policyId, expectedVersion: 1, carryOver: 99), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new LeaveDbContext(Options(dbName));
        var saved = await verify.LeavePolicies.SingleAsync();
        Assert.Equal(11, saved.CarryOverDays);
        Assert.Equal(2, saved.Version);
    }
}
