using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Exercises <see cref="AssetNumberGenerator"/> against the shared real Postgres testcontainer
/// (raw UPDATE ... RETURNING round-trip), following the CompaniesDbContext scope pattern from
/// AdminCancelSubscriptionEndpointTests.
/// </summary>
[Collection("Integration")]
public class AssetNumberGeneratorTests
{
    private readonly ApiWebApplicationFactory _factory;

    public AssetNumberGeneratorTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<Guid> SeedSettingsAsync(string? prefix, int nextAssetNumber, int minimumLength)
    {
        var now = DateTimeOffset.UtcNow;
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var settings = await db.CompanySettings.SingleOrDefaultAsync(s => s.CompanyId == companyId);
        if (settings is null)
        {
            settings = CompanySettings.CreateDefault(companyId, now);
            db.CompanySettings.Add(settings);
        }

        settings.UpdateAssetNumberSettings(
            AssetNumberMode.Automatic, prefix, nextAssetNumber, minimumLength, now);
        await db.SaveChangesAsync();
        return companyId;
    }

    private async Task<int> ReadNextAssetNumberAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var settings = await db.CompanySettings
            .AsNoTracking()
            .SingleAsync(s => s.CompanyId == companyId);
        return settings.NextAssetNumber;
    }

    [Fact]
    public async Task GenerateNextAsync_Sequential_Allocations_Advance_Persisted_Counter()
    {
        var companyId = await SeedSettingsAsync(prefix: null, nextAssetNumber: 1, minimumLength: 4);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var generator = new AssetNumberGenerator(db);

        var first = await generator.GenerateNextAsync(companyId, CancellationToken.None);
        var second = await generator.GenerateNextAsync(companyId, CancellationToken.None);
        var third = await generator.GenerateNextAsync(companyId, CancellationToken.None);

        Assert.Equal("0001", first);
        Assert.Equal("0002", second);
        Assert.Equal("0003", third);
        Assert.Equal(4, await ReadNextAssetNumberAsync(companyId));
    }

    [Fact]
    public async Task GenerateNextAsync_Concurrent_Allocations_Return_Unique_Numbers()
    {
        const int parallelism = 10;
        var companyId = await SeedSettingsAsync(prefix: null, nextAssetNumber: 1, minimumLength: 4);

        var tasks = Enumerable.Range(0, parallelism).Select(async _ =>
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var generator = new AssetNumberGenerator(db);
            return await generator.GenerateNextAsync(companyId, CancellationToken.None);
        });

        var results = await Task.WhenAll(tasks);

        Assert.Equal(parallelism, results.Distinct().Count());
        Assert.Equal(1 + parallelism, await ReadNextAssetNumberAsync(companyId));
    }

    [Fact]
    public async Task GenerateNextAsync_Does_Not_Advance_Another_Companys_Counter()
    {
        var companyA = await SeedSettingsAsync(prefix: null, nextAssetNumber: 1, minimumLength: 4);
        var companyB = await SeedSettingsAsync(prefix: null, nextAssetNumber: 1, minimumLength: 4);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var generator = new AssetNumberGenerator(db);

        await generator.GenerateNextAsync(companyA, CancellationToken.None);
        await generator.GenerateNextAsync(companyA, CancellationToken.None);

        Assert.Equal(3, await ReadNextAssetNumberAsync(companyA));
        Assert.Equal(1, await ReadNextAssetNumberAsync(companyB));
    }

    [Fact]
    public async Task GenerateNextAsync_Applies_Prefix_And_Minimum_Length_Padding()
    {
        var companyId = await SeedSettingsAsync(prefix: "AST-", nextAssetNumber: 42, minimumLength: 5);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var generator = new AssetNumberGenerator(db);

        var result = await generator.GenerateNextAsync(companyId, CancellationToken.None);

        Assert.Equal("AST-00042", result);
    }

    [Fact]
    public async Task GenerateNextAsync_Does_Not_Truncate_Number_Longer_Than_Minimum_Length()
    {
        var companyId = await SeedSettingsAsync(prefix: "A", nextAssetNumber: 123456, minimumLength: 3);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var generator = new AssetNumberGenerator(db);

        var result = await generator.GenerateNextAsync(companyId, CancellationToken.None);

        Assert.Equal("A123456", result);
    }

    [Fact]
    public async Task GenerateNextAsync_Throws_When_Company_Settings_Row_Missing()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var generator = new AssetNumberGenerator(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.GenerateNextAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
