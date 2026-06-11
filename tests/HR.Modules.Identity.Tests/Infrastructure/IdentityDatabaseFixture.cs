using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HR.Modules.Identity.Tests.Infrastructure;

/// <summary>
/// Spins up a single PostgreSQL container per test collection and provides
/// a fresh, migrated <see cref="IdentityDbContext"/> for each test.
/// </summary>
public sealed class IdentityDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("hr_identity_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        // Apply all migrations once for the whole fixture lifetime.
        await using var ctx = BuildContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    internal IdentityDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity"))
            .Options;

        return new IdentityDbContext(options);
    }
}
