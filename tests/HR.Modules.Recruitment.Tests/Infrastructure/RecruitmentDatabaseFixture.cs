using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

/// <summary>
/// Ticket 19 (P2): spins up a single PostgreSQL container and provides a fresh, migrated
/// <see cref="RecruitmentDbContext"/> — same shape as
/// HR.Modules.Identity.Tests.Infrastructure.IdentityDatabaseFixture. Used only where a genuine
/// concurrent-Postgres race needs to be exercised (EF's InMemory provider used by the rest of this
/// module's tests does not reliably model real concurrent-write contention).
/// </summary>
public sealed class RecruitmentDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("hr_recruitment_tests")
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

    internal RecruitmentDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "recruitment"))
            .Options;

        return new RecruitmentDbContext(options);
    }
}
