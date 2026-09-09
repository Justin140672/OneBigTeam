using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.UpdateSicknessCategory;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

// Ticket 2 (optimistic concurrency rollout): SicknessCategory.Version coverage for
// UpdateSicknessCategory. Two DbContext instances over the same EF InMemory database; context B
// saves first (bumping the store's Version), then the handler under test saves against context A
// with a stale ExpectedVersion.
public class UpdateSicknessCategoryConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<SicknessDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<SicknessDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task<(string DbName, Guid CompanyId, Guid Id)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var category = SicknessCategory.Create(Guid.NewGuid(), companyId, "Cold", 1, new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));

        await using var seed = new SicknessDbContext(Options(dbName));
        seed.SicknessCategories.Add(category);
        await seed.SaveChangesAsync();

        return (dbName, companyId, category.Id);
    }

    private static UpdateSicknessCategoryRequest Request(Guid companyId, Guid id, int? expectedVersion, int order = 1)
        => new()
        {
            CompanyId = companyId,
            Id = id,
            Name = "Cold",
            DisplayOrder = order,
            ExpectedVersion = expectedVersion,
        };

    [Fact]
    public void IncrementVersion_Increments_Version_By_One()
    {
        var category = SicknessCategory.Create(Guid.NewGuid(), Guid.NewGuid(), "X", 0, new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        Assert.Equal(1, category.Version);

        category.IncrementVersion();
        Assert.Equal(2, category.Version);
    }

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, id) = await SeedAsync();

        await using var db = new SicknessDbContext(Options(dbName));
        var result = await new UpdateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow), new FakeAuditEventPublisher())
            .HandleAsync(Request(companyId, id, expectedVersion: 1, order: 5), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
        Assert.Equal(5, result.Value.DisplayOrder);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, id) = await SeedAsync();

        await using var db = new SicknessDbContext(Options(dbName));
        var result = await new UpdateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow), new FakeAuditEventPublisher())
            .HandleAsync(Request(companyId, id, expectedVersion: null, order: 7), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new SicknessDbContext(Options(dbName));
        var saved = await verify.SicknessCategories.SingleAsync();
        Assert.Equal(1, saved.DisplayOrder);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Publishes_No_Audit_Event()
    {
        var (dbName, companyId, id) = await SeedAsync();

        await using var ctxA = new SicknessDbContext(Options(dbName));
        await ctxA.SicknessCategories.SingleAsync();

        await using (var ctxB = new SicknessDbContext(Options(dbName)))
        {
            var winner = await new UpdateSicknessCategoryHandler(ctxB, new FakeClock(FixedUtcNow), new FakeAuditEventPublisher())
                .HandleAsync(Request(companyId, id, expectedVersion: 1, order: 9), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new FakeAuditEventPublisher();
        var result = await new UpdateSicknessCategoryHandler(ctxA, new FakeClock(FixedUtcNow), audit)
            .HandleAsync(Request(companyId, id, expectedVersion: 1, order: 99), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.PublishedEvents);

        await using var verify = new SicknessDbContext(Options(dbName));
        var saved = await verify.SicknessCategories.SingleAsync();
        Assert.Equal(9, saved.DisplayOrder);
        Assert.Equal(2, saved.Version);
    }
}
