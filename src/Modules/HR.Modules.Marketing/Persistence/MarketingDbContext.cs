using HR.Modules.Marketing.Domain;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Persistence;

internal sealed class MarketingDbContext : DbContext
{
    public MarketingDbContext(DbContextOptions<MarketingDbContext> options)
        : base(options)
    {
    }

    public DbSet<MarketingProduct> MarketingProducts => Set<MarketingProduct>();
    public DbSet<MarketingFeature> MarketingFeatures => Set<MarketingFeature>();
    public DbSet<MarketingRoadmapItem> MarketingRoadmapItems => Set<MarketingRoadmapItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("marketing");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MarketingDbContext).Assembly);
    }
}
