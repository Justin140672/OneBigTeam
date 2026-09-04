using HR.Modules.Sickness.Domain;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Persistence;

internal class SicknessDbContext : DbContext
{
    public SicknessDbContext(DbContextOptions<SicknessDbContext> options)
        : base(options)
    {
    }

    public DbSet<SicknessCategory> SicknessCategories => Set<SicknessCategory>();
    public DbSet<SicknessRecord> SicknessRecords => Set<SicknessRecord>();
    public DbSet<SicknessEvidenceRequest> SicknessEvidenceRequests => Set<SicknessEvidenceRequest>();
    public DbSet<ReturnToWorkReview> ReturnToWorkReviews => Set<ReturnToWorkReview>();
    public DbSet<AttendanceAlert> AttendanceAlerts => Set<AttendanceAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sickness");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SicknessDbContext).Assembly);
    }
}
