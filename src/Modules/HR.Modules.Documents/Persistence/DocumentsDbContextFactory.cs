using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.Documents.Persistence;

internal sealed class DocumentsDbContextFactory : IDesignTimeDbContextFactory<DocumentsDbContext>
{
    public DocumentsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("DOCUMENTS_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<DocumentsDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "documents"));

        return new DocumentsDbContext(optionsBuilder.Options);
    }
}
