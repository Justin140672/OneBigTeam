using HR.Modules.DataImport.Domain;
using HR.SharedKernel.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Persistence;

internal sealed class DataImportDbContext : DbContext
{
    public DataImportDbContext(DbContextOptions<DataImportDbContext> options)
        : base(options)
    {
    }

    public DbSet<ImportSession> ImportSessions => Set<ImportSession>();
    public DbSet<ImportRowError> ImportRowErrors => Set<ImportRowError>();
    public DbSet<ImportStagingEmployee> ImportStagingEmployees => Set<ImportStagingEmployee>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("data_import");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataImportDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration<IdempotencyRecord>());
    }
}
