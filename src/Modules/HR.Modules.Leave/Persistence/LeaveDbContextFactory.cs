using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.Leave.Persistence;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef migrations).
/// Set the LEAVE_CONNECTION_STRING environment variable or update the fallback
/// to point at your local Postgres instance before running migrations.
/// </summary>
internal sealed class LeaveDbContextFactory : IDesignTimeDbContextFactory<LeaveDbContext>
{
    public LeaveDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("LEAVE_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<LeaveDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "leave"));

        return new LeaveDbContext(optionsBuilder.Options);
    }
}
