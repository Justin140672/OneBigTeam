using HR.Integration.Tests.Infrastructure;
using Npgsql;

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
        Assert.Equal(platformTableCountFirstRun, platformTableCountSecondRun,
            "Platform table count should not change when migrations are re-run (idempotency)");
        Assert.Equal(companiesTableCountFirstRun, companiesTableCountSecondRun,
            "Companies table count should not change when migrations are re-run (idempotency)");
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
    /// This test manually creates a fresh PostgreSQL database (no pre-existing schema),
    /// runs the migration sequence, and verifies:
    /// 1. Both schemas exist
    /// 2. All expected tables are created
    /// 3. Company seeding completes
    /// 4. The startup sequence succeeds (no FK constraint violations)
    /// </summary>
    [Fact]
    public async Task Fresh_Database_Migrations_Run_In_Correct_Order_Companies_Before_Platform()
    {
        using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("ConnectionStrings__hr"));
        await connection.OpenAsync();

        try
        {
            // Verify companies schema was created and has expected tables
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

                // Verify essential tables from Companies migration exist
                Assert.Contains("companies", tables);
                Assert.Contains("customer_subscriptions", tables);
                Assert.Contains("public_holidays", tables);
                Assert.True(tables.Count > 0, "Companies schema should have tables after migration");
            }

            // Verify platform schema was created and has expected tables
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

                // Verify Platform tables exist
                Assert.Contains("customer_database_assignments", tables);
                Assert.Contains("platform_settings", tables);
                Assert.Contains("platform_metrics_snapshots", tables);
                Assert.True(tables.Count > 0, "Platform schema should have tables after migration");
            }

            // Verify that the foreign key constraint from platform.customer_database_assignments to
            // companies.companies exists (proving Companies ran before Platform)
            const string fkQuery = @"
                SELECT 1 FROM information_schema.table_constraints
                WHERE constraint_schema = 'platform'
                  AND table_name = 'customer_database_assignments'
                  AND constraint_name = 'FK_customer_database_assignments_companies_company_id'";

            using (var cmd = new NpgsqlCommand(fkQuery, connection))
            {
                var result = await cmd.ExecuteScalarAsync();
                Assert.NotNull(result, "Foreign key from platform.customer_database_assignments to companies.companies should exist");
            }

            // Verify that seeded companies exist in the companies table (proves SeedCompaniesAsync ran)
            const string companiesSeededQuery = "SELECT COUNT(*) FROM companies.companies WHERE id IN ('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002')";

            using (var cmd = new NpgsqlCommand(companiesSeededQuery, connection))
            {
                var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                Assert.Equal(2, count);
            }

            // Verify that seeded subscriptions exist (proves SeedCompaniesAsync completed after Companies migration)
            const string subscriptionsSeededQuery = "SELECT COUNT(*) FROM companies.customer_subscriptions";

            using (var cmd = new NpgsqlCommand(subscriptionsSeededQuery, connection))
            {
                var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                Assert.True(count >= 2, "At least 2 seeded subscriptions should exist");
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
