using HR.Modules.Reporting.Domain;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Persistence;

internal sealed class ReportingDbContext : DbContext
{
    public ReportingDbContext(DbContextOptions<ReportingDbContext> options)
        : base(options)
    {
    }

    public DbSet<ReportFavourite> ReportFavourites => Set<ReportFavourite>();
    public DbSet<SavedReportView> SavedReportViews => Set<SavedReportView>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("reporting");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReportingDbContext).Assembly);
    }
}
