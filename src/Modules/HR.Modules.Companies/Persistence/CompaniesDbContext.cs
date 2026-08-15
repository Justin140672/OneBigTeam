using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Persistence;

internal sealed class CompaniesDbContext : DbContext
{
    public CompaniesDbContext(DbContextOptions<CompaniesDbContext> options)
        : base(options)
    {
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyAddress> CompanyAddresses => Set<CompanyAddress>();
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();
    public DbSet<CompanyBranding> CompanyBranding => Set<CompanyBranding>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<PublicHoliday> PublicHolidays => Set<PublicHoliday>();
    public DbSet<CustomerSubscription> CustomerSubscriptions => Set<CustomerSubscription>();
    public DbSet<CustomerBillingSnapshot> CustomerBillingSnapshots => Set<CustomerBillingSnapshot>();
    public DbSet<SupportSession> SupportSessions => Set<SupportSession>();
    public DbSet<PlatformMetricsSnapshot> PlatformMetricsSnapshots => Set<PlatformMetricsSnapshot>();
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("companies");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CompaniesDbContext).Assembly);
    }
}
