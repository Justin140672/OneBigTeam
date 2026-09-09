using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Features.UpdateAssetCategory;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

// Ticket 2 (optimistic concurrency rollout): AssetCategory.Version coverage for UpdateAssetCategory.
// Two DbContext instances over the same EF InMemory database; context B saves first (bumping the
// store's Version), then the handler under test saves against context A with a stale
// ExpectedVersion. UpdateAssetCategory publishes no events, so the stale path only asserts that
// nothing was committed.
public class UpdateAssetCategoryConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<AssetsDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<AssetsDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task<(string DbName, Guid CompanyId, Guid Id)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();

        await using var seed = new AssetsDbContext(Options(dbName));
        var result = await new CreateAssetCategoryHandler(seed, new FakeClock(FixedUtcNow))
            .HandleAsync(new CreateAssetCategoryRequest { CompanyId = companyId, Name = "Electronics" }, CancellationToken.None);

        return (dbName, companyId, result.Value!.Id);
    }

    private static UpdateAssetCategoryRequest Request(Guid companyId, Guid id, int? expectedVersion, string? description = null)
        => new()
        {
            CompanyId = companyId,
            Id = id,
            Name = "Electronics",
            Description = description,
            ExpectedVersion = expectedVersion,
        };

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, id) = await SeedAsync();

        await using var db = new AssetsDbContext(Options(dbName));
        var result = await new UpdateAssetCategoryHandler(db, new FakeClock(FixedUtcNow))
            .HandleAsync(Request(companyId, id, expectedVersion: 1, description: "v2"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
        Assert.Equal("v2", result.Value.Description);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, id) = await SeedAsync();

        await using var db = new AssetsDbContext(Options(dbName));
        var result = await new UpdateAssetCategoryHandler(db, new FakeClock(FixedUtcNow))
            .HandleAsync(Request(companyId, id, expectedVersion: null, description: "second"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new AssetsDbContext(Options(dbName));
        var saved = await verify.AssetCategories.SingleAsync();
        Assert.Null(saved.Description);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Leaves_Row_Unchanged()
    {
        var (dbName, companyId, id) = await SeedAsync();

        await using var ctxA = new AssetsDbContext(Options(dbName));
        await ctxA.AssetCategories.SingleAsync();

        await using (var ctxB = new AssetsDbContext(Options(dbName)))
        {
            var winner = await new UpdateAssetCategoryHandler(ctxB, new FakeClock(FixedUtcNow))
                .HandleAsync(Request(companyId, id, expectedVersion: 1, description: "winner"), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var result = await new UpdateAssetCategoryHandler(ctxA, new FakeClock(FixedUtcNow))
            .HandleAsync(Request(companyId, id, expectedVersion: 1, description: "loser"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new AssetsDbContext(Options(dbName));
        var saved = await verify.AssetCategories.SingleAsync();
        Assert.Equal("winner", saved.Description);
        Assert.Equal(2, saved.Version);
    }
}
