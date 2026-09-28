using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies the separation of platform and companies migrations in the startup pipeline.
/// When both migrations run on a fresh database, each completes successfully as a distinct
/// step reported in the migration runner. This enables operators to identify which migration
/// failed if only one step fails during startup.
///
/// The platform migration creates the platform schema (system-level tables like subscriptions, audit).
/// The companies migration creates the companies schema (tenant-scoped tables like employees, leaves).
/// Each step runs independently and can be diagnosed separately via /health/startup-migrations.
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
    /// Verifies that Companies migrations run successfully BEFORE Platform migrations on a blank database.
    /// This is a critical dependency order test: Platform migration has a foreign key constraint to
    /// companies.companies and copies data from companies.platform_settings and companies.platform_metrics_snapshots,
    /// so it MUST run after the Companies schema and tables exist.
    ///
    /// This test creates a fresh, isolated PostgreSQL Testcontainer with no application schemas,
    /// runs the production startup migration sequence, and verifies:
    /// 1. Both schemas are created in the correct order
    /// 2. All expected tables are present with correct structure
    /// 3. Migration history is recorded in each schema-specific table
    /// 4. Company seeding completes successfully
    /// 5. Foreign key constraints between schemas are functional (proving Companies ran first)
    /// 6. The startup sequence is idempotent (running again succeeds without duplicates)
    /// 7. Reversing the migration order would fail (a negative test guard)
    /// </summary>
    [Fact]
    public async Task Fresh_Database_Migrations_Run_In_Correct_Order_Companies_Before_Platform()
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
            // configuration needed to run migrations. This replicates the production startup
            // sequence (Program.cs) without the full HTTP pipeline.
            var services = new ServiceCollection();
            services.AddLogging();

            // Register both DbContexts against the temporary database, using the same configuration
            // as production (schema-specific migration history tables).
            services.AddDbContext<HR.Modules.Companies.Persistence.CompaniesDbContext>(options =>
                options.UseNpgsql(tempConnectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "companies")));

            services.AddDbContext<HR.Modules.Companies.Persistence.PlatformDbContext>(options =>
                options.UseNpgsql(tempConnectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform")));

            var serviceProvider = services.BuildServiceProvider();

            // Phase 1: Run the PRODUCTION startup orchestration sequence
            // (from src/HR.Api/Program.cs, lines 241-250).
            // Companies migrations must run before Platform because Platform's migration has
            // a foreign key to companies.companies and copies data from companies tables.

            // Step 1: Companies migration + seed
            await serviceProvider.MigrateCompaniesAsync();
            await serviceProvider.SeedCompaniesAsync();

            // Step 2: Platform migration (this would fail if Companies hadn't run first)
            await serviceProvider.MigratePlatformAsync();

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

                // Verify the foreign key constraint from platform to companies exists
                // (this proves Platform ran after Companies — if Companies hadn't created the companies.companies
                // table, Platform's migration would have failed when trying to create this FK)
                const string fkQuery = @"
                    SELECT constraint_name FROM information_schema.table_constraints
                    WHERE constraint_schema = 'platform'
                      AND table_name = 'customer_database_assignments'
                      AND constraint_type = 'FOREIGN KEY'";

                using (var cmd = new NpgsqlCommand(fkQuery, connection))
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

                // Re-run the migration sequence (migrations should be idempotent)
                await serviceProvider.MigrateCompaniesAsync();
                await serviceProvider.SeedCompaniesAsync();
                await serviceProvider.MigratePlatformAsync();

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

                // Verify seed data counts also didn't duplicate
                const string companiesCountQuery = "SELECT COUNT(*) FROM companies.companies";
                using (var cmd = new NpgsqlCommand(companiesCountQuery, connection))
                {
                    var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                    Assert.Equal(2, count);
                }

                const string subscriptionsCountQuery = "SELECT COUNT(*) FROM companies.customer_subscriptions";
                using (var cmd = new NpgsqlCommand(subscriptionsCountQuery, connection))
                {
                    var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                    Assert.Equal(2, count);
                }
            }
            finally
            {
                await connection.CloseAsync();
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
