using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HR.Modules.Tasks.Tests.Infrastructure;

/// <summary>
/// Ticket 19 (P2): spins up a single PostgreSQL container and provides a fresh, migrated
/// <see cref="TasksDbContext"/> — same shape as
/// HR.Modules.Identity.Tests.Infrastructure.IdentityDatabaseFixture /
/// HR.Modules.Recruitment.Tests.Infrastructure.RecruitmentDatabaseFixture. Used only where a
/// genuine concurrent-Postgres race needs to be exercised.
/// </summary>
public sealed class TasksDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("hr_tasks_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        await using var ctx = BuildContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    internal TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "tasks"))
            .Options;

        return new TasksDbContext(options);
    }
}
