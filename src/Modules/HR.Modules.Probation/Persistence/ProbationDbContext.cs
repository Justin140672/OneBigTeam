using HR.Modules.Probation.Domain;
using HR.SharedKernel.Idempotency;
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
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("probation");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProbationDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration<IdempotencyRecord>());
    }
}
