using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.UpdateLeaveType;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

// Ticket 2 (optimistic concurrency rollout): LeaveType.Version coverage for UpdateLeaveType.
// Two DbContext instances over the same EF InMemory database; context B saves first (bumping the
// store's Version), then the handler under test saves against context A with a stale
// ExpectedVersion.
public class UpdateLeaveTypeConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<LeaveDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<LeaveDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task<(string DbName, Guid CompanyId, Guid Id)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);

        await using var seed = new LeaveDbContext(Options(dbName));
        seed.LeaveTypes.Add(entity);
        await seed.SaveChangesAsync();

        return (dbName, companyId, entity.Id);
    }

    private static UpdateLeaveTypeRequest Request(Guid companyId, Guid id, int? expectedVersion, int days = 25)
        => new()
        {
            CompanyId = companyId,
            Id = id,
            Name = "Annual Leave",
            Code = "ANNUAL",
            DefaultEntitlementDays = days,
            AccrualMethod = AccrualMethod.Monthly,
            Behaviour = LeaveTypeBehaviour.Standard,
            ExpectedVersion = expectedVersion,
        };

    [Fact]
    public void IncrementVersion_Increments_Version_By_One()
    {
        var entity = LeaveType.Create(Guid.NewGuid(), Guid.NewGuid(), "X", "X", 1, AccrualMethod.None, LeaveTypeBehaviour.Standard, new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        Assert.Equal(1, entity.Version);

        entity.IncrementVersion();
        Assert.Equal(2, entity.Version);
    }

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, id) = await SeedAsync();

        await using var ctx = new LeaveDbContext(Options(dbName));
        var result = await new UpdateLeaveTypeHandler(ctx, new FakeClock(FixedUtcNow), new NoOpAuditEventPublisher())
            .HandleAsync(Request(companyId, id, expectedVersion: 1, days: 28), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
        Assert.Equal(28, result.Value.DefaultEntitlementDays);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, id) = await SeedAsync();

        await using var ctx = new LeaveDbContext(Options(dbName));
        var result = await new UpdateLeaveTypeHandler(ctx, new FakeClock(FixedUtcNow), new NoOpAuditEventPublisher())
            .HandleAsync(Request(companyId, id, expectedVersion: null, days: 30), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new LeaveDbContext(Options(dbName));
        var saved = await verify.LeaveTypes.SingleAsync();
        Assert.Equal(25, saved.DefaultEntitlementDays);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Publishes_No_Audit_Event()
    {
        var (dbName, companyId, id) = await SeedAsync();

        await using var ctxA = new LeaveDbContext(Options(dbName));
        await ctxA.LeaveTypes.SingleAsync();

        await using (var ctxB = new LeaveDbContext(Options(dbName)))
        {
            var winner = await new UpdateLeaveTypeHandler(ctxB, new FakeClock(FixedUtcNow), new NoOpAuditEventPublisher())
                .HandleAsync(Request(companyId, id, expectedVersion: 1, days: 27), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new CapturingAuditEventPublisher();
        var result = await new UpdateLeaveTypeHandler(ctxA, new FakeClock(FixedUtcNow), audit)
            .HandleAsync(Request(companyId, id, expectedVersion: 1, days: 99), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new LeaveDbContext(Options(dbName));
        var saved = await verify.LeaveTypes.SingleAsync();
        Assert.Equal(27, saved.DefaultEntitlementDays);
        Assert.Equal(2, saved.Version);
    }
}
