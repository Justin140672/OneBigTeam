using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class MoveAccountStateToUserProfilesMigrationTests(IdentityDatabaseFixture fixture)
{
    private const string PreviousMigration = "20260928123443_AddPendingChanges";
    private const string TargetMigration = "20260929162926_MoveAccountStateToUserProfiles";

    [Fact]
    public async Task Backfills_LastLogin_And_Disablement_Onto_Profiles_Then_Drops_Users_Table()
    {
        var databaseName = $"hr_identity_migration_{Guid.NewGuid():N}";
        var admin = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        await using (var adminConnection = new NpgsqlConnection(admin.ConnectionString))
        {
            await adminConnection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", adminConnection);
            await create.ExecuteNonQueryAsync();
        }

        var scoped = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = databaseName };
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(scoped.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity"))
            .Options;

        var lastLogin = new DateTimeOffset(2026, 9, 1, 8, 15, 0, TimeSpan.Zero);
        var disabledUserUpdatedAt = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
        var activeUserId = Guid.NewGuid();
        var disabledUserId = Guid.NewGuid();
        var alreadyDisabledProfileId = Guid.NewGuid();
        var profileOnlyId = Guid.NewGuid();

        await using (var context = new IdentityDbContext(options))
        {
            await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);

            await using var connection = new NpgsqlConnection(scoped.ConnectionString);
            await connection.OpenAsync();

            await InsertUserAsync(connection, activeUserId, "active", isActive: true, lastLogin, disabledUserUpdatedAt);
            await InsertUserAsync(connection, disabledUserId, "disabled", isActive: false, null, disabledUserUpdatedAt);
            await InsertUserAsync(connection, alreadyDisabledProfileId, "already", isActive: true, null, disabledUserUpdatedAt);

            await InsertProfileAsync(connection, activeUserId, "active", isActive: true);
            await InsertProfileAsync(connection, disabledUserId, "disabled", isActive: true);
            await InsertProfileAsync(connection, alreadyDisabledProfileId, "already", isActive: false);
            await InsertProfileAsync(connection, profileOnlyId, "profileonly", isActive: true);
        }

        await using (var context = new IdentityDbContext(options))
        {
            await context.GetService<IMigrator>().MigrateAsync(TargetMigration);

            var profiles = await context.UserProfiles.AsNoTracking().ToDictionaryAsync(p => p.Id);

            Assert.True(profiles[activeUserId].IsActive);
            Assert.Equal(lastLogin, profiles[activeUserId].LastLoginAt);

            Assert.False(profiles[disabledUserId].IsActive);
            Assert.Equal(disabledUserUpdatedAt, profiles[disabledUserId].DisabledAt);

            Assert.False(profiles[alreadyDisabledProfileId].IsActive);

            Assert.True(profiles[profileOnlyId].IsActive);
            Assert.Null(profiles[profileOnlyId].LastLoginAt);

            await using var connection = new NpgsqlConnection(scoped.ConnectionString);
            await connection.OpenAsync();
            await using var check = new NpgsqlCommand("SELECT to_regclass('identity.users') IS NULL", connection);
            Assert.True((bool)(await check.ExecuteScalarAsync())!);
        }

        NpgsqlConnection.ClearAllPools();
        await using var dropConnection = new NpgsqlConnection(admin.ConnectionString);
        await dropConnection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", dropConnection);
        await drop.ExecuteNonQueryAsync();
    }

    private static async Task InsertUserAsync(
        NpgsqlConnection connection, Guid id, string name, bool isActive, DateTimeOffset? lastLogin, DateTimeOffset updatedAt)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO identity.users
                (id, email, normalized_email, password_hash, first_name, last_name, is_active, is_email_confirmed, last_login_at, created_at, updated_at)
            VALUES
                (@id, @email, @normalized, 'x', 'First', 'Last', @active, TRUE, @lastLogin, @updatedAt, @updatedAt)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("email", $"{name}@test.com");
        command.Parameters.AddWithValue("normalized", $"{name}@test.com".ToUpperInvariant());
        command.Parameters.AddWithValue("active", isActive);
        command.Parameters.AddWithValue("lastLogin", (object?)lastLogin ?? DBNull.Value);
        command.Parameters.AddWithValue("updatedAt", updatedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertProfileAsync(NpgsqlConnection connection, Guid id, string name, bool isActive)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO identity.user_profiles
                (id, supabase_auth_user_id, company_id, email, first_name, last_name, created_at, updated_at, is_active, disabled_at)
            VALUES
                (@id, @supabaseId, @companyId, @email, 'First', 'Last', @now, @now, @active, CASE WHEN @active THEN NULL ELSE @now END)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("supabaseId", Guid.NewGuid());
        command.Parameters.AddWithValue("companyId", Guid.NewGuid());
        command.Parameters.AddWithValue("email", $"{name}@test.com");
        command.Parameters.AddWithValue("now", new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        command.Parameters.AddWithValue("active", isActive);
        await command.ExecuteNonQueryAsync();
    }
}
