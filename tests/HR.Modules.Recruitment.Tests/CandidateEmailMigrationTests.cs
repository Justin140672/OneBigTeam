using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HR.Modules.Recruitment.Tests;

public class CandidateEmailMigrationTests(RecruitmentDatabaseFixture fixture)
    : IClassFixture<RecruitmentDatabaseFixture>
{
    private const string MigrationUnderTest = "20260925153706_EnforceCandidateNormalisedEmailUniqueness";
    private const string OldIndexName = "IX_candidates_company_id_email";
    private const string NewIndexName = "ux_candidates_company_id_normalised_email";

    [Fact]
    public async Task Existing_Case_Variant_Duplicates_Fail_The_Migration_With_Actionable_Error_And_Leave_Schema_Unchanged()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.ConnectionString);
        await scratch.MigrateToPreviousAsync();

        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        await scratch.InsertCandidateAsync(companyA, "dup@example.com");
        await scratch.InsertCandidateAsync(companyA, " DUP@Example.com");
        await scratch.InsertCandidateAsync(companyB, "clean@example.com");

        var postgres = await scratch.MigrateToUnderTestExpectingFailureAsync();

        Assert.Contains("candidate_email_duplicates", postgres.MessageText);
        Assert.Contains("1 duplicate", postgres.MessageText);
        Assert.Contains("2 candidate row(s)", postgres.MessageText);
        Assert.NotNull(postgres.Detail);
        Assert.Contains(companyA.ToString(), postgres.Detail);
        Assert.Contains("(1 group(s))", postgres.Detail);
        Assert.DoesNotContain(companyB.ToString(), postgres.Detail);

        Assert.DoesNotContain("dup@example.com", postgres.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dup@example.com", postgres.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dup@example.com", postgres.Hint ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        Assert.False(await scratch.ColumnExistsAsync("normalised_email"));
        Assert.True(await scratch.IndexExistsAsync(OldIndexName));
        Assert.False(await scratch.IndexExistsAsync(NewIndexName));
        Assert.False(await scratch.MigrationAppliedAsync(MigrationUnderTest));
        Assert.Equal(3, await scratch.CountCandidatesAsync());
    }

    [Fact]
    public async Task Error_Counts_Every_Duplicate_Group_And_Lists_Each_Affected_Company()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.ConnectionString);
        await scratch.MigrateToPreviousAsync();

        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await scratch.InsertCandidateAsync(companyA, "one@example.com");
        await scratch.InsertCandidateAsync(companyA, "ONE@example.com");
        await scratch.InsertCandidateAsync(companyA, "One@Example.com ");
        await scratch.InsertCandidateAsync(companyA, "two@example.com");
        await scratch.InsertCandidateAsync(companyA, "Two@example.com");
        await scratch.InsertCandidateAsync(companyB, "three@example.com");
        await scratch.InsertCandidateAsync(companyB, "  three@example.com");

        var postgres = await scratch.MigrateToUnderTestExpectingFailureAsync();

        Assert.Contains("3 duplicate", postgres.MessageText);
        Assert.Contains("7 candidate row(s)", postgres.MessageText);
        Assert.Contains($"{companyA} (2 group(s))", postgres.Detail);
        Assert.Contains($"{companyB} (1 group(s))", postgres.Detail);
        Assert.False(await scratch.ColumnExistsAsync("normalised_email"));
    }

    [Fact]
    public async Task After_Resolving_Duplicates_Migration_Succeeds_Backfills_And_Replaces_Index()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.ConnectionString);
        await scratch.MigrateToPreviousAsync();

        var companyA = Guid.NewGuid();
        await scratch.InsertCandidateAsync(companyA, "dup@example.com");
        var secondId = await scratch.InsertCandidateAsync(companyA, " DUP@Example.com");
        await scratch.InsertCandidateAsync(companyA, "Mixed.Case@Example.COM");

        await scratch.MigrateToUnderTestExpectingFailureAsync();

        await scratch.ExecuteAsync(
            "UPDATE recruitment.candidates SET email = 'dup.second@example.com' WHERE id = @id",
            ("id", secondId));

        await scratch.MigrateToUnderTestAsync();

        Assert.True(await scratch.MigrationAppliedAsync(MigrationUnderTest));
        Assert.True(await scratch.ColumnExistsAsync("normalised_email"));
        Assert.Equal("NO", await scratch.ColumnIsNullableAsync("normalised_email"));

        Assert.Equal(0, await scratch.ScalarAsync<long>(
            "SELECT count(*) FROM recruitment.candidates WHERE normalised_email IS DISTINCT FROM lower(btrim(email))"));
        Assert.Equal(1, await scratch.ScalarAsync<long>(
            "SELECT count(*) FROM recruitment.candidates WHERE normalised_email = 'mixed.case@example.com'"));

        Assert.False(await scratch.IndexExistsAsync(OldIndexName));
        var indexDef = await scratch.IndexDefinitionAsync(NewIndexName);
        Assert.NotNull(indexDef);
        Assert.Contains("UNIQUE", indexDef);
        Assert.Contains("(company_id, normalised_email)", indexDef);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => scratch.InsertCandidateWithNormalisedAsync(companyA, "MIXED.case@example.com"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        Assert.Equal(NewIndexName, ex.ConstraintName);
    }

    [Fact]
    public async Task Same_Email_In_Different_Companies_Is_Not_A_Duplicate()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.ConnectionString);
        await scratch.MigrateToPreviousAsync();

        await scratch.InsertCandidateAsync(Guid.NewGuid(), "shared@example.com");
        await scratch.InsertCandidateAsync(Guid.NewGuid(), "SHARED@example.com");
        await scratch.InsertCandidateAsync(Guid.NewGuid(), " shared@example.com ");

        await scratch.MigrateToUnderTestAsync();

        Assert.True(await scratch.MigrationAppliedAsync(MigrationUnderTest));
        Assert.Equal(3, await scratch.ScalarAsync<long>(
            "SELECT count(*) FROM recruitment.candidates WHERE normalised_email = 'shared@example.com'"));
    }

    [Fact]
    public async Task Empty_Table_Migrates_And_Down_Restores_Previous_Schema()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.ConnectionString);
        await scratch.MigrateToPreviousAsync();

        await scratch.MigrateToUnderTestAsync();
        Assert.True(await scratch.IndexExistsAsync(NewIndexName));

        await scratch.MigrateToPreviousAsync();

        Assert.False(await scratch.MigrationAppliedAsync(MigrationUnderTest));
        Assert.False(await scratch.ColumnExistsAsync("normalised_email"));
        Assert.False(await scratch.IndexExistsAsync(NewIndexName));
        Assert.True(await scratch.IndexExistsAsync(OldIndexName));
    }

    private sealed class ScratchDatabase : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _databaseName;

        private ScratchDatabase(string adminConnectionString, string databaseName, string connectionString)
        {
            _adminConnectionString = adminConnectionString;
            _databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<ScratchDatabase> CreateAsync(string fixtureConnectionString)
        {
            var databaseName = $"recruitment_email_migration_{Guid.NewGuid():N}";

            await using (var admin = new NpgsqlConnection(fixtureConnectionString))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
                await create.ExecuteNonQueryAsync();
            }

            var connectionString = new NpgsqlConnectionStringBuilder(fixtureConnectionString)
            {
                Database = databaseName,
                Pooling  = false,
                IncludeErrorDetail = true,
            }.ConnectionString;

            return new ScratchDatabase(fixtureConnectionString, databaseName, connectionString);
        }

        private RecruitmentDbContext BuildContext() =>
            new(new DbContextOptionsBuilder<RecruitmentDbContext>()
                .UseNpgsql(ConnectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "recruitment"))
                .Options);

        public async Task MigrateToPreviousAsync()
        {
            await using var db = BuildContext();
            var migrations = db.Database.GetMigrations().ToList();
            var index = migrations.IndexOf(MigrationUnderTest);
            Assert.True(index > 0, $"Migration '{MigrationUnderTest}' was not found (or has no predecessor) in the Recruitment migrations.");

            await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
        }

        public async Task MigrateToUnderTestAsync()
        {
            await using var db = BuildContext();
            await db.GetService<IMigrator>().MigrateAsync(MigrationUnderTest);
        }

        public async Task<PostgresException> MigrateToUnderTestExpectingFailureAsync()
        {
            var ex = await Record.ExceptionAsync(MigrateToUnderTestAsync);
            Assert.NotNull(ex);

            for (var current = ex; current is not null; current = current.InnerException)
            {
                if (current is PostgresException postgres)
                {
                    Assert.Equal(PostgresErrorCodes.RaiseException, postgres.SqlState);
                    return postgres;
                }
            }

            Assert.Fail($"Expected a PostgresException from the migration but got: {ex}");
            throw new InvalidOperationException("unreachable");
        }

        public async Task<Guid> InsertCandidateAsync(Guid companyId, string email)
        {
            var id = Guid.NewGuid();
            await ExecuteAsync(
                """
                INSERT INTO recruitment.candidates
                    (id, company_id, first_name, last_name, email, is_active, version, created_at, updated_at)
                VALUES
                    (@id, @company_id, 'Test', 'Candidate', @email, TRUE, 1, now(), now())
                """,
                ("id", id), ("company_id", companyId), ("email", email));
            return id;
        }

        public async Task InsertCandidateWithNormalisedAsync(Guid companyId, string email) =>
            await ExecuteAsync(
                """
                INSERT INTO recruitment.candidates
                    (id, company_id, first_name, last_name, email, normalised_email, is_active, version, created_at, updated_at)
                VALUES
                    (@id, @company_id, 'Test', 'Candidate', @email, lower(btrim(@email)), TRUE, 1, now(), now())
                """,
                ("id", Guid.NewGuid()), ("company_id", companyId), ("email", email));

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            var result = await command.ExecuteScalarAsync();
            return (T)result!;
        }

        public async Task<long> CountCandidatesAsync() =>
            await ScalarAsync<long>("SELECT count(*) FROM recruitment.candidates");

        public async Task<bool> ColumnExistsAsync(string column) =>
            await ScalarAsync<long>(
                """
                SELECT count(*) FROM information_schema.columns
                WHERE table_schema = 'recruitment' AND table_name = 'candidates' AND column_name = @column
                """,
                ("column", column)) == 1;

        public async Task<string> ColumnIsNullableAsync(string column) =>
            await ScalarAsync<string>(
                """
                SELECT is_nullable FROM information_schema.columns
                WHERE table_schema = 'recruitment' AND table_name = 'candidates' AND column_name = @column
                """,
                ("column", column));

        public async Task<bool> IndexExistsAsync(string indexName) =>
            await ScalarAsync<long>(
                "SELECT count(*) FROM pg_indexes WHERE schemaname = 'recruitment' AND indexname = @name",
                ("name", indexName)) == 1;

        public async Task<string?> IndexDefinitionAsync(string indexName)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT indexdef FROM pg_indexes WHERE schemaname = 'recruitment' AND indexname = @name", connection);
            command.Parameters.AddWithValue("name", indexName);
            return await command.ExecuteScalarAsync() as string;
        }

        public async Task<bool> MigrationAppliedAsync(string migrationId) =>
            await ScalarAsync<long>(
                "SELECT count(*) FROM recruitment.__ef_migrations_history WHERE \"MigrationId\" = @id",
                ("id", migrationId)) == 1;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var admin = new NpgsqlConnection(_adminConnectionString);
                await admin.OpenAsync();
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
                await drop.ExecuteNonQueryAsync();
            }
            catch
            {
            }
        }
    }
}
