using HR.Modules.Tasks.Domain;
using HR.SharedKernel.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Persistence;

internal sealed class TasksDbContext : DbContext
{
    public TasksDbContext(DbContextOptions<TasksDbContext> options)
        : base(options)
    {
    }

    public DbSet<TaskItem> TaskItems => Set<TaskItem>();
    public DbSet<TaskCompletionOperation> TaskCompletionOperations => Set<TaskCompletionOperation>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("tasks");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TasksDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration<IdempotencyRecord>());
    }
}
