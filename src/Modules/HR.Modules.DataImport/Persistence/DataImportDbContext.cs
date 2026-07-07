using HR.Modules.DataImport.Domain;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("data_import");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataImportDbContext).Assembly);
    }
}
