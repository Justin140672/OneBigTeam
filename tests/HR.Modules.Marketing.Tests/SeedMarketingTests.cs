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
        Assert.Equal(10, roadmap.Count);
        Assert.All(roadmap, r => Assert.True(r.IsPublished));
        Assert.All(roadmap, r => Assert.Equal(MarketingDeliveryStatus.ComingSoon, r.DeliveryStatus));
        Assert.Equal(HR.SharedKernel.PhaseTwoRoadmapCatalog.All.Select(r => r.Title),
            roadmap.OrderBy(r => r.DisplayOrder).Select(r => r.Title));
    }

    [Fact]
    public async Task Reseed_Preserves_Renamed_Completed_And_Unpublished_Items()
    {
        await using var provider = BuildProvider();
        await provider.SeedMarketingAsync();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MarketingDbContext>();
            var item = await db.MarketingRoadmapItems.OrderBy(r => r.DisplayOrder).FirstAsync();
            item.Update("Custom title", "Edited description", "star", MarketingDeliveryStatus.Available,
                20, Guid.NewGuid(), DateTimeOffset.UtcNow);
            item.Unpublish(Guid.NewGuid(), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        await provider.SeedMarketingAsync();
        using var check = provider.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<MarketingDbContext>();
        Assert.Equal(10, await context.MarketingRoadmapItems.CountAsync());
        var edited = await context.MarketingRoadmapItems.SingleAsync(r => r.Title == "Custom title");
        Assert.Equal("Edited description", edited.Description);
        Assert.Equal(MarketingDeliveryStatus.Available, edited.DeliveryStatus);
        Assert.False(edited.IsPublished);
    }

    [Fact]
    public async Task Seed_Upgrades_Legacy_Roadmap_Without_Duplicates()
    {
        await using var provider = BuildProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MarketingDbContext>();
            foreach (var seed in HR.SharedKernel.PhaseTwoRoadmapCatalog.All.Where(r => r.LegacyTitle is not null))
            {
                var item = MarketingRoadmapItem.Create(Guid.NewGuid(), MarketingProduct.SingletonId,
                    seed.LegacyTitle!, "Old description", "chart-line", MarketingDeliveryStatus.Planned,
                    0, null, DateTimeOffset.UtcNow).Value!;
                item.Publish(null, DateTimeOffset.UtcNow);
                db.MarketingRoadmapItems.Add(item);
            }
            await db.SaveChangesAsync();
        }
        await provider.SeedMarketingAsync();
        await provider.SeedMarketingAsync();
        using var check = provider.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<MarketingDbContext>();
        var roadmap = await context.MarketingRoadmapItems.OrderBy(r => r.DisplayOrder).ToListAsync();
        Assert.Equal(10, roadmap.Count);
        Assert.Equal(HR.SharedKernel.PhaseTwoRoadmapCatalog.All.Select(r => r.Description), roadmap.Select(r => r.Description));
    }
}
