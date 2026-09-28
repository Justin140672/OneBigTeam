using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence.Configurations;
using HR.SharedKernel.Idempotency;
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
    public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();
    public DbSet<CustomerBillingSnapshot> CustomerBillingSnapshots => Set<CustomerBillingSnapshot>();
    public DbSet<SupportSession> SupportSessions => Set<SupportSession>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("companies");

        // Apply Companies-owned entity configurations explicitly
        modelBuilder.ApplyConfiguration(new CompanyConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyAddressConfiguration());
        modelBuilder.ApplyConfiguration(new CompanySettingsConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyBrandingConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new PublicHolidayConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerSubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedStripeEventConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerBillingSnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new SupportSessionConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration<IdempotencyRecord>());
    }
}
