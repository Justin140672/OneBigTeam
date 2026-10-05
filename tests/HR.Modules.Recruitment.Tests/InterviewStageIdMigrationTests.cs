using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HR.Modules.Recruitment.Tests;

public class InterviewStageIdMigrationTests(RecruitmentDatabaseFixture fixture)
    : IClassFixture<RecruitmentDatabaseFixture>
{
    private const string MigrationUnderTest = "20261005080901_AddInterviewStageId";
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Single_Interview_Stage_Company_Backfills_All_Interviews_To_That_Stage()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.ConnectionString);
        await scratch.MigrateToPreviousAsync();

        var company = Guid.NewGuid();
        var cv = await scratch.InsertStageAsync(company, "CV Review", 2, "CvReview");
        var only = await scratch.InsertStageAsync(company, "Interview", 3, "Interview");
        var offer = await scratch.InsertStageAsync(company, "Offer", 4, "Offer");
        var app = await scratch.InsertApplicationAsync(company, offer);
        await scratch.InsertHistoryAsync(company, app, cv, only, T0);
        await scratch.InsertHistoryAsync(company, app, only, offer, T0.AddDays(3));
        var viaHistory = await scratch.InsertInterviewAsync(company, app, T0, "Passed");
        var beforeAnyHistory = await scratch.InsertInterviewAsync(company, app, T0.AddDays(-1), "Failed");
        var whileInCvStage = await scratch.InsertInterviewAsync(company, await scratch.InsertApplicationAsync(company, cv), T0, "Passed");

        await scratch.MigrateToUnderTestAsync();

        Assert.Equal(only, await scratch.StageOfAsync(viaHistory));
        Assert.Equal(only, await scratch.StageOfAsync(beforeAnyHistory));
        Assert.Equal(only, await scratch.StageOfAsync(whileInCvStage));
    }

    [Fact]
    public async Task Multi_Stage_History_Assigns_Each_Interview_To_The_Stage_Occupied_When_It_Was_Created()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.ConnectionString);
        await scratch.MigrateToPreviousAsync();

        var company = Guid.NewGuid();
        var cv = await scratch.InsertStageAsync(company, "CV Review", 2, "CvReview");
        var first = await scratch.InsertStageAsync(company, "First Interview", 3, "Interview");
        var second = await scratch.InsertStageAsync(company, "Second Interview", 4, "Interview");
        var app = await scratch.InsertApplicationAsync(company, second);
        await scratch.InsertHistoryAsync(company, app, cv, first, T0);
        await scratch.InsertHistoryAsync(company, app, first, second, T0.AddDays(2));
        var firstInterview = await scratch.InsertInterviewAsync(company, app, T0, "Passed");
        var secondInterview = await scratch.InsertInterviewAsync(company, app, T0.AddDays(2), "Passed");
        var retryInSecond = await scratch.InsertInterviewAsync(company, app, T0.AddDays(4), "Pending");

        await scratch.MigrateToUnderTestAsync();

        Assert.Equal(first, await scratch.StageOfAsync(firstInterview));
        Assert.Equal(second, await scratch.StageOfAsync(secondInterview));
        Assert.Equal(second, await scratch.StageOfAsync(retryInSecond));
    }

    [Fact]
    public async Task Ambiguous_Multi_Stage_History_Leaves_Stage_Null_So_It_Cannot_Unlock_An_Offer()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.ConnectionString);
        await scratch.MigrateToPreviousAsync();

        var company = Guid.NewGuid();
        var cv = await scratch.InsertStageAsync(company, "CV Review", 2, "CvReview");
        await scratch.InsertStageAsync(company, "First Interview", 3, "Interview");
        var second = await scratch.InsertStageAsync(company, "Second Interview", 4, "Interview");

        var noHistoryApp = await scratch.InsertApplicationAsync(company, second);
        var passedNoHistory = await scratch.InsertInterviewAsync(company, noHistoryApp, T0, "Passed");

        var cvApp = await scratch.InsertApplicationAsync(company, second);
        await scratch.InsertHistoryAsync(company, cvApp, null, cv, T0);
        var scheduledWhileInCv = await scratch.InsertInterviewAsync(company, cvApp, T0.AddHours(1), "Passed");

        var pendingApp = await scratch.InsertApplicationAsync(company, second);
        var pendingNoHistory = await scratch.InsertInterviewAsync(company, pendingApp, T0, "Pending");

        await scratch.MigrateToUnderTestAsync();

        Assert.Null(await scratch.StageOfAsync(passedNoHistory));
        Assert.Null(await scratch.StageOfAsync(scheduledWhileInCv));
        Assert.Equal(second, await scratch.StageOfAsync(pendingNoHistory));
    }

    [Fact]
    public async Task Migration_Adds_Column_And_Down_Removes_It()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.ConnectionString);
        await scratch.MigrateToPreviousAsync();
        Assert.False(await scratch.StageColumnExistsAsync());

        await scratch.MigrateToUnderTestAsync();
        Assert.True(await scratch.StageColumnExistsAsync());

        await scratch.MigrateToPreviousAsync();
        Assert.False(await scratch.StageColumnExistsAsync());
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
            var databaseName = $"recruitment_interview_stage_migration_{Guid.NewGuid():N}";

            await using (var admin = new NpgsqlConnection(fixtureConnectionString))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
                await create.ExecuteNonQueryAsync();
            }

            var connectionString = new NpgsqlConnectionStringBuilder(fixtureConnectionString)
            {
                Database = databaseName,
                Pooling = false,
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
            Assert.True(index > 0, $"Migration '{MigrationUnderTest}' was not found (or has no predecessor).");
            await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
        }

        public async Task MigrateToUnderTestAsync()
        {
            await using var db = BuildContext();
            await db.GetService<IMigrator>().MigrateAsync(MigrationUnderTest);
        }

        public async Task<Guid> InsertStageAsync(Guid companyId, string name, int order, string purpose)
        {
            var id = Guid.NewGuid();
            await InsertAsync("recruitment_stages",
                ("id", id), ("company_id", companyId), ("name", name), ("display_order", order),
                ("is_active", true), ("is_terminal", false), ("purpose", purpose), ("terminal_outcome", "None"));
            return id;
        }

        public async Task<Guid> InsertApplicationAsync(Guid companyId, Guid currentStageId)
        {
            var vacancyId = Guid.NewGuid();
            await InsertAsync("vacancies", ("id", vacancyId), ("company_id", companyId));
            var candidateId = Guid.NewGuid();
            await InsertAsync("candidates",
                ("id", candidateId), ("company_id", companyId), ("email", $"{candidateId:N}@example.com"),
                ("normalised_email", $"{candidateId:N}@example.com"));
            var id = Guid.NewGuid();
            await InsertAsync("applications",
                ("id", id), ("company_id", companyId), ("vacancy_id", vacancyId), ("candidate_id", candidateId),
                ("current_stage_id", currentStageId));
            return id;
        }

        public Task InsertHistoryAsync(Guid companyId, Guid applicationId, Guid? previousStageId, Guid newStageId, DateTimeOffset changedAt) =>
            InsertAsync("application_stage_history_entries",
                ("id", Guid.NewGuid()), ("company_id", companyId), ("application_id", applicationId),
                ("previous_stage_id", (object?)previousStageId ?? DBNull.Value), ("new_stage_id", newStageId),
                ("changed_at", changedAt));

        public async Task<Guid> InsertInterviewAsync(Guid companyId, Guid applicationId, DateTimeOffset createdAt, string outcome)
        {
            var id = Guid.NewGuid();
            await InsertAsync("interviews",
                ("id", id), ("company_id", companyId), ("application_id", applicationId),
                ("interviewer_employee_id", Guid.NewGuid()), ("scheduled_at", createdAt), ("outcome", outcome),
                ("created_at", createdAt), ("updated_at", createdAt));
            return id;
        }

        public async Task<Guid?> StageOfAsync(Guid interviewId)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT stage_id FROM recruitment.interviews WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", interviewId);
            var result = await command.ExecuteScalarAsync();
            return result is Guid g ? g : null;
        }

        public async Task<bool> StageColumnExistsAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'recruitment' AND table_name = 'interviews' AND column_name = 'stage_id'",
                connection);
            return (long)(await command.ExecuteScalarAsync())! == 1;
        }

        // Inserts a row, filling any other NOT NULL column without a default using a neutral value for its type.
        private async Task InsertAsync(string table, params (string Column, object Value)[] values)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();

            var provided = values.ToDictionary(v => v.Column, v => v.Value);
            var filler = new Dictionary<string, string>();

            await using (var columns = new NpgsqlCommand(
                """
                SELECT column_name, data_type FROM information_schema.columns
                WHERE table_schema = 'recruitment' AND table_name = @table AND is_nullable = 'NO' AND column_default IS NULL
                """, connection))
            {
                columns.Parameters.AddWithValue("table", table);
                await using var reader = await columns.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var name = reader.GetString(0);
                    if (provided.ContainsKey(name)) continue;

                    filler[name] = reader.GetString(1) switch
                    {
                        "uuid" => $"'{Guid.NewGuid()}'",
                        "character varying" or "text" => "'x'",
                        "integer" or "bigint" or "smallint" => "1",
                        "boolean" => "false",
                        "numeric" => "0",
                        "date" => "current_date",
                        var t when t.StartsWith("timestamp") => "now()",
                        var t => throw new InvalidOperationException($"No filler for {table}.{name} ({t})."),
                    };
                }
            }

            var allColumns = provided.Keys.Concat(filler.Keys).ToList();
            var sql = $"INSERT INTO recruitment.{table} ({string.Join(", ", allColumns)}) VALUES " +
                      $"({string.Join(", ", provided.Keys.Select(k => "@" + k).Concat(filler.Values))})";

            await using var insert = new NpgsqlCommand(sql, connection);
            foreach (var (name, value) in provided)
                insert.Parameters.AddWithValue(name, value);
            await insert.ExecuteNonQueryAsync();
        }

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
