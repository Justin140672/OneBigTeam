using HR.Modules.Marketing;
using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Marketing.Tests;

public class SeedMarketingTests
{
    private static ServiceProvider BuildProvider()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<MarketingDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SeedMarketingAsync_Is_Idempotent_And_Seeds_The_Expected_Published_Content()
    {
        await using var provider = BuildProvider();

        await provider.SeedMarketingAsync();
        await provider.SeedMarketingAsync();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketingDbContext>();

        Assert.Equal(1, await db.MarketingProducts.CountAsync());
        Assert.True(await db.MarketingProducts.AnyAsync(p => p.Id == MarketingProduct.SingletonId));

        var features = await db.MarketingFeatures.ToListAsync();
        Assert.Equal(7, features.Count);
        Assert.All(features, f => Assert.True(f.IsPublished));
        Assert.All(features, f => Assert.Equal(MarketingDeliveryStatus.Available, f.DeliveryStatus));
        Assert.Equal(features.Count, features.Select(f => f.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var roadmap = await db.MarketingRoadmapItems.ToListAsync();
        Assert.Equal(3, roadmap.Count);
        Assert.All(roadmap, r => Assert.True(r.IsPublished));
    }
}
