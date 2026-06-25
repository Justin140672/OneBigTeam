using HR.Modules.Probation.Domain;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Persistence;

internal sealed class ProbationDbContext : DbContext
{
    public ProbationDbContext(DbContextOptions<ProbationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ProbationRecord> ProbationRecords => Set<ProbationRecord>();
    public DbSet<ProbationReview> ProbationReviews => Set<ProbationReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("probation");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProbationDbContext).Assembly);
    }
}
