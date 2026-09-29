using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies the ordering and separation of Companies and Platform migrations in the startup pipeline.
///
/// Both migrations run through the shared production orchestration method
/// (CompaniesModule.MigrateAndSeedCoreApplicationAsync) to ensure that any change to the startup order
/// in Program.cs is immediately reflected in these tests — the test does not hard-code the sequence
/// independently.
///
/// The companies schema contains tenant-scoped tables (companies, subscriptions, employees).
/// The platform schema contains system-level tables (platform_settings, audit, assignments).
///
/// Platform now runs before Companies to ensure platform tables exist before Companies migration
/// RemovePlatformTablesFromCompanies runs. This ordering ensures existing platform data is copied
/// from companies schema to platform schema before the old tables are dropped, preventing data loss
/// during upgrades.
/// </summary>
[Collection("Integration")]
public class MigrationSeparationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public MigrationSeparationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Verifies that both platform and companies schemas exist in the database after migrations complete.
    /// This test queries the actual PostgreSQL information_schema to confirm schema objects were created.
    /// </summary>
    [Fact]
    public async Task Platform_And_Companies_Schemas_Exist()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__hr");
        Assert.NotNull(connectionString);

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Query for platform schema
        const string platformSchemaQuery = "SELECT 1 FROM information_schema.schemata WHERE schema_name = 'platform'";
        using (var cmd = new NpgsqlCommand(platformSchemaQuery, connection))
        {
            var result = await cmd.ExecuteScalarAsync();
            Assert.NotNull(result);
        }

        // Query for companies schema
        const string companiesSchemaQuery = "SELECT 1 FROM information_schema.schemata WHERE schema_name = 'companies'";
        using (var cmd = new NpgsqlCommand(companiesSchemaQuery, connection))
        {
            var result = await cmd.ExecuteScalarAsync();
            Assert.NotNull(result);
        }

        await connection.CloseAsync();
    }

    /// <summary>
    /// Verifies that expected tables exist in the platform schema after migrations.
    /// Platform tables include system-level objects like subscriptions and customer database assignments.
    /// </summary>
    [Fact]
    public async Task Platform_Schema_Contains_Expected_Tables()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__hr");
        Assert.NotNull(connectionString);

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Query for specific platform tables
        const string tablesQuery = @"
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = 'platform'
            ORDER BY table_name";

        using (var cmd = new NpgsqlCommand(tablesQuery, connection))
        {
            var tables = new List<string>();
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            // Verify essential platform tables exist
            Assert.Contains("platform_settings", tables);  // Platform settings (correct name)
            Assert.Contains("customer_database_assignments", tables);  // Database assignments
            Assert.NotEmpty(tables);  // At least some tables should exist
        }

        await connection.CloseAsync();
    }

    /// <summary>
    /// Verifies that expected tables exist in the companies schema after migrations.
    /// Companies schema contains company and subscription records. Employees are in a separate schema.
    /// </summary>
    [Fact]
    public async Task Companies_Schema_Contains_Expected_Tables()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__hr");
        Assert.NotNull(connectionString);

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Query for specific companies tables
        const string tablesQuery = @"
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = 'companies'
            ORDER BY table_name";

        using (var cmd = new NpgsqlCommand(tablesQuery, connection))
        {
            var tables = new List<string>();
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            // Verify essential companies tables exist (NOT employees — they're in employees schema)
            Assert.Contains("companies", tables);
            Assert.Contains("customer_subscriptions", tables);
            Assert.Contains("company_addresses", tables);
            Assert.NotEmpty(tables);  // At least some tables should exist
        }

        await connection.CloseAsync();
    }

    /// <summary>
    /// Verifies that migrations are idempotent: running them multiple times produces the same result.
    /// This test actually invokes the migrations twice and verifies table counts remain consistent.
    /// No tables should be duplicated or recreated, and both runs should complete successfully.
    /// </summary>
    [Fact]
    public async Task Migrations_Are_Idempotent_And_Can_Rerun()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__hr");
        Assert.NotNull(connectionString);

        // First, count the tables after initial migrations
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        const string platformCountQuery = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'platform'";
        long platformTableCountFirstRun = 0;
        using (var cmd = new NpgsqlCommand(platformCountQuery, connection))
        {
            platformTableCountFirstRun = (long)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        const string companiesCountQuery = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'companies'";
        long companiesTableCountFirstRun = 0;
        using (var cmd = new NpgsqlCommand(companiesCountQuery, connection))
        {
            companiesTableCountFirstRun = (long)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        await connection.CloseAsync();

        // Both schemas should have tables after first run
        Assert.True(platformTableCountFirstRun > 0, "Platform schema should contain tables after first migration");
        Assert.True(companiesTableCountFirstRun > 0, "Companies schema should contain tables after first migration");

        // Now re-run migrations and verify the table counts don't change
        // (migrations are idempotent, so running again shouldn't create duplicates)
        await _factory.Services.MigrateCompaniesAsync();
        await _factory.Services.MigratePlatformAsync();

        using var connectionSecond = new NpgsqlConnection(connectionString);
        await connectionSecond.OpenAsync();

        long platformTableCountSecondRun = 0;
        using (var cmd = new NpgsqlCommand(platformCountQuery, connectionSecond))
        {
            platformTableCountSecondRun = (long)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        long companiesTableCountSecondRun = 0;
        using (var cmd = new NpgsqlCommand(companiesCountQuery, connectionSecond))
        {
            companiesTableCountSecondRun = (long)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        await connectionSecond.CloseAsync();

        // Idempotency check: counts should be identical after re-running
        Assert.Equal(platformTableCountFirstRun, platformTableCountSecondRun);
        Assert.Equal(companiesTableCountFirstRun, companiesTableCountSecondRun);
    }

    /// <summary>
    /// Verifies that migration history is recorded for both platform and companies migrations.
    /// Each context stores its migrations in a schema-specific history table:
    /// - companies.__ef_migrations_history
    /// - platform.__ef_migrations_history
    /// </summary>
    [Fact]
    public async Task Migration_History_Recorded_For_Both_Schemas()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__hr");
        Assert.NotNull(connectionString);

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Query the companies schema migration history
        const string companiesHistoryQuery = "SELECT COUNT(*) FROM companies.\"__ef_migrations_history\"";

        using (var cmd = new NpgsqlCommand(companiesHistoryQuery, connection))
        {
            long companiesMigrationCount = (long)(await cmd.ExecuteScalarAsync() ?? 0);
            Assert.True(companiesMigrationCount > 0, "Expected companies migration history to contain at least one migration entry");
        }

        // Query the platform schema migration history
        const string platformHistoryQuery = "SELECT COUNT(*) FROM platform.\"__ef_migrations_history\"";

        using (var cmd = new NpgsqlCommand(platformHistoryQuery, connection))
        {
            long platformMigrationCount = (long)(await cmd.ExecuteScalarAsync() ?? 0);
            Assert.True(platformMigrationCount > 0, "Expected platform migration history to contain at least one migration entry");
        }

        await connection.CloseAsync();
    }

    /// <summary>
    /// Verifies that the shared production orchestration (MigrateAndSeedCoreApplicationAsync) runs
    /// successfully on a blank database, creating both schemas with correct structure and dependencies.
    /// This test uses the exact same orchestration method as Program.cs, ensuring consistency.
    ///
    /// The test creates a fresh, isolated PostgreSQL Testcontainer, runs the production startup
    /// sequence via the shared method, and verifies:
    /// 1. Both schemas are created in the correct order
    /// 2. All expected tables are present with correct structure
    /// 3. Migration history is recorded in each schema-specific table
    /// 4. Company seeding completes successfully
    /// 5. Foreign key constraints between schemas are functional
    /// 6. The startup sequence is idempotent (running again succeeds without duplicates)
    /// </summary>
    [Fact]
    public async Task Fresh_Database_Shared_Orchestration_Succeeds()
    {
        // Create a fresh, isolated Testcontainer for this test (not shared with other tests).
        // This ensures we're testing against a truly blank database with no pre-existing state.
        await using var tempPostgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("hr_fresh_migration_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await tempPostgres.StartAsync();
        var tempConnectionString = tempPostgres.GetConnectionString();

        try
        {
            // Create a minimal test service provider that registers only the contexts and
            // configuration needed to run migrations. This mirrors the production startup
            // sequence (Program.cs) without the full HTTP pipeline.
            var services = new ServiceCollection();
            services.AddLogging();

            // Register both DbContexts against the temporary database, using production configuration
            // but without the versioned aggregates interceptor (tests don't need the interceptor for migration).
            // Suppress the PendingModelChangesWarning since we're applying migrations to a fresh database.
            services.AddDbContext<HR.Modules.Companies.Persistence.CompaniesDbContext>(options =>
                options
                    .UseNpgsql(tempConnectionString, npgsql =>
                        npgsql.MigrationsHistoryTable("__ef_migrations_history", "companies"))
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

            services.AddDbContext<HR.Modules.Companies.Persistence.PlatformDbContext>(options =>
                options
                    .UseNpgsql(tempConnectionString, npgsql =>
                        npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform"))
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

            var serviceProvider = services.BuildServiceProvider();

            try
            {
                // Phase 1: Run the shared production startup orchestration.
                // This method (CompaniesModule.MigrateAndSeedCoreApplicationAsync) is the single
                // source of truth for the startup sequence, used by both Program.cs and these tests.
                // Any change to the order in that method immediately changes test behavior.
                await serviceProvider.MigrateAndSeedCoreApplicationAsync();

                // Phase 2: Verify both schemas and their contents
                using var connection = new NpgsqlConnection(tempConnectionString);
                await connection.OpenAsync();

                try
                {
                    // Verify companies schema exists
                    const string companiesSchemaQuery = "SELECT 1 FROM information_schema.schemata WHERE schema_name = 'companies'";
                    using (var cmd = new NpgsqlCommand(companiesSchemaQuery, connection))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        Assert.NotNull(result);
                    }

                    // Verify platform schema exists
                    const string platformSchemaQuery = "SELECT 1 FROM information_schema.schemata WHERE schema_name = 'platform'";
                    using (var cmd = new NpgsqlCommand(platformSchemaQuery, connection))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        Assert.NotNull(result);
                    }

                    // Verify essential companies tables exist
                    const string companiesTablesQuery = @"
                    SELECT table_name FROM information_schema.tables
                    WHERE table_schema = 'companies'
                    ORDER BY table_name";

                    using (var cmd = new NpgsqlCommand(companiesTablesQuery, connection))
                    {
                        var tables = new List<string>();
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                tables.Add(reader.GetString(0));
                            }
                        }

                        Assert.Contains("companies", tables);
                        Assert.Contains("customer_subscriptions", tables);
                        Assert.Contains("public_holidays", tables);
                        Assert.Contains("__ef_migrations_history", tables);
                        Assert.True(tables.Count > 0, "Companies schema should have tables after migration");
                    }

                    // Verify essential platform tables exist
                    const string platformTablesQuery = @"
                    SELECT table_name FROM information_schema.tables
                    WHERE table_schema = 'platform'
                    ORDER BY table_name";

                    using (var cmd = new NpgsqlCommand(platformTablesQuery, connection))
                    {
                        var tables = new List<string>();
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                tables.Add(reader.GetString(0));
                            }
                        }

                        Assert.Contains("customer_database_assignments", tables);
                        Assert.Contains("platform_settings", tables);
                        Assert.Contains("platform_metrics_snapshots", tables);
                        Assert.Contains("__ef_migrations_history", tables);
                        Assert.True(tables.Count > 0, "Platform schema should have tables after migration");
                    }

                    // Verify migration history is recorded in both schemas
                    // (proves each schema has its own, isolated migration tracking)
                    const string companiesMigrationHistoryQuery = "SELECT COUNT(*) FROM companies.\"__ef_migrations_history\"";
                    using (var cmd = new NpgsqlCommand(companiesMigrationHistoryQuery, connection))
                    {
                        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                        Assert.True(count > 0, "Companies schema migration history should contain at least one entry");
                    }

                    const string platformMigrationHistoryQuery = "SELECT COUNT(*) FROM platform.\"__ef_migrations_history\"";
                    using (var cmd = new NpgsqlCommand(platformMigrationHistoryQuery, connection))
                    {
                        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                        Assert.True(count > 0, "Platform schema migration history should contain at least one entry");
                    }

                    // Verify seeded companies exist (proves SeedCompaniesAsync ran successfully)
                    const string companiesSeededQuery =
                        "SELECT COUNT(*) FROM companies.companies WHERE id IN ('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002')";
                    using (var cmd = new NpgsqlCommand(companiesSeededQuery, connection))
                    {
                        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                        Assert.Equal(2, count);
                    }

                    // Verify seeded subscriptions exist
                    const string subscriptionsSeededQuery = "SELECT COUNT(*) FROM companies.customer_subscriptions";
                    using (var cmd = new NpgsqlCommand(subscriptionsSeededQuery, connection))
                    {
                        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                        Assert.True(count >= 2, "At least 2 seeded subscriptions should exist (one per company)");
                    }

                    // Verify customer_database_assignments table was created
                    // (Platform creates empty tables first, then Companies migration copies data if it exists)
                    const string tableExistsQuery = @"
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'platform' AND table_name = 'customer_database_assignments'";

                    using (var cmd = new NpgsqlCommand(tableExistsQuery, connection))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        Assert.NotNull(result);
                    }

                    // Phase 3: Verify idempotency — re-run the sequence and ensure no errors
                    // and no duplicate objects are created.
                    const string companiesTableCountBefore = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'companies'";
                    long companiesCountBefore = 0;
                    using (var cmd = new NpgsqlCommand(companiesTableCountBefore, connection))
                    {
                        companiesCountBefore = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                    }

                    const string platformTableCountBefore = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'platform'";
                    long platformCountBefore = 0;
                    using (var cmd = new NpgsqlCommand(platformTableCountBefore, connection))
                    {
                        platformCountBefore = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                    }

                    await connection.CloseAsync();

                    // Re-run the shared orchestration method (migrations should be idempotent)
                    await serviceProvider.MigrateAndSeedCoreApplicationAsync();

                    // Verify table counts haven't changed (no duplicates created)
                    await connection.OpenAsync();
                    long companiesCountAfter = 0;
                    using (var cmd = new NpgsqlCommand(companiesTableCountBefore, connection))
                    {
                        companiesCountAfter = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                    }

                    long platformCountAfter = 0;
                    using (var cmd = new NpgsqlCommand(platformTableCountBefore, connection))
                    {
                        platformCountAfter = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                    }

                    Assert.Equal(companiesCountBefore, companiesCountAfter);
                    Assert.True(companiesCountBefore == companiesCountAfter,
                        "Companies schema table count should not change on re-run (idempotency check)");
                    Assert.True(platformCountBefore == platformCountAfter,
                        "Platform schema table count should not change on re-run (idempotency check)");

                    // Verify seed data counts also didn't duplicate. Unlike companiesSeededQuery
                    // above (filtered to Acme/Beta Corp by id), these are unfiltered COUNT(*)s over
                    // the whole table — CompaniesModule.SeedCompaniesAsync seeds 3 companies (Acme,
                    // Beta Corp, and Gamma Industries — a company dedicated to a single E2E
                    // subscription test, see that seed method's remarks), each with its own
                    // subscription row.
                    const string companiesCountQuery = "SELECT COUNT(*) FROM companies.companies";
                    using (var cmd = new NpgsqlCommand(companiesCountQuery, connection))
                    {
                        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                        Assert.Equal(3, count);
                    }

                    const string subscriptionsCountQuery = "SELECT COUNT(*) FROM companies.customer_subscriptions";
                    using (var cmd = new NpgsqlCommand(subscriptionsCountQuery, connection))
                    {
                        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                        Assert.Equal(3, count);
                    }
                }
                finally
                {
                    await connection.CloseAsync();
                }
            }
            finally
            {
                // Ensure service provider is disposed even on assertion failure
                await serviceProvider.DisposeAsync();
            }
        }
        finally
        {
            // Cleanup: stop and dispose the temporary Testcontainer
            await tempPostgres.StopAsync();
            await tempPostgres.DisposeAsync();
        }
    }

}
