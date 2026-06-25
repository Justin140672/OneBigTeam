using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.Notifications.Persistence;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef migrations).
/// Set the NOTIFICATIONS_CONNECTION_STRING environment variable or update the fallback
/// to point at your local Postgres instance before running migrations.
/// </summary>
internal sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("NOTIFICATIONS_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<NotificationsDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "notifications"));

        return new NotificationsDbContext(optionsBuilder.Options);
    }
}
