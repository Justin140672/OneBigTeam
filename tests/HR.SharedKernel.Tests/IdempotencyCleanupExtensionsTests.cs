using HR.SharedKernel.Idempotency;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.SharedKernel.Tests;

/// <summary>
/// Ticket 3 (P1) follow-up item 4: proves expired idempotency records are actually deleted in
/// bounded batches while unexpired ones are left alone (and stay replayable). Uses SQLite's
/// in-memory mode rather than EF's InMemory provider - <c>ExecuteDeleteAsync</c> (which the cleanup
/// path is built on) needs a real relational provider to translate to SQL; EF InMemory throws
/// <see cref="InvalidOperationException"/> for it.
/// </summary>
public class IdempotencyCleanupExtensionsTests
{
    private sealed class Record : IIdempotencyRecord
    {
        public string OperationId { get; set; } = "op";
        public Guid CompanyId { get; set; }
        public Guid ActorId { get; set; }
        public string Key { get; set; } = string.Empty;
        public string RequestFingerprint { get; set; } = "fp";
        public int ResponseStatusCode { get; set; } = 200;
        public string ResponseBodyJson { get; set; } = "{}";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<Record> Records => Set<Record>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration<Record>());

            // SQLite's EF provider can't translate DateTimeOffset comparisons directly (a documented
            // provider limitation, unrelated to the cleanup logic under test) - store as UTC ticks
            // so `<=` comparisons translate to a plain integer comparison. Postgres/Npgsql, the real
            // production provider, has no such limitation and needs no such conversion.
            modelBuilder.Entity<Record>().Property(r => r.ExpiresAt)
                .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
            modelBuilder.Entity<Record>().Property(r => r.CreatedAt)
                .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        }
    }

    private static async Task<(SqliteConnection Connection, TestDbContext Context)> OpenContextAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options;
        var context = new TestDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return (connection, context);
    }

    [Fact]
    public async Task Expired_Records_Are_Deleted_Unexpired_Records_Remain()
    {
        var (connection, context) = await OpenContextAsync();
        await using var _ = connection;
        await using var db = context;

        var now = DateTimeOffset.UtcNow;
        db.Records.AddRange(
            new Record { Key = "expired-1", ExpiresAt = now.AddDays(-1) },
            new Record { Key = "expired-2", ExpiresAt = now.AddSeconds(-1) },
            new Record { Key = "still-valid", ExpiresAt = now.AddDays(7) });
        await db.SaveChangesAsync();

        var removed = await db.Records.CleanupExpiredIdempotencyRecordsWithLoggingAsync(
            now, NullLogger.Instance, "Test", CancellationToken.None);

        Assert.Equal(2, removed);
        var remaining = await db.Records.ToListAsync();
        var survivor = Assert.Single(remaining);
        Assert.Equal("still-valid", survivor.Key);
    }

    [Fact]
    public async Task Backlog_Larger_Than_One_Batch_Is_Fully_Drained_In_One_Call()
    {
        var (connection, context) = await OpenContextAsync();
        await using var _ = connection;
        await using var db = context;

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
            db.Records.Add(new Record { Key = $"expired-{i}", ExpiresAt = now.AddDays(-1) });
        await db.SaveChangesAsync();

        // Force multiple batches with a tiny batch size to prove the "loop until a batch comes back
        // short" logic actually drains a backlog spanning more than one batch, not just one page.
        var removed = await db.Records.CleanupExpiredIdempotencyRecordsWithLoggingAsync(
            now, NullLogger.Instance, "Test", CancellationToken.None, batchSize: 2);

        Assert.Equal(5, removed);
        Assert.Empty(await db.Records.ToListAsync());
    }

    [Fact]
    public async Task No_Expired_Records_Removes_Nothing()
    {
        var (connection, context) = await OpenContextAsync();
        await using var _ = connection;
        await using var db = context;

        var now = DateTimeOffset.UtcNow;
        db.Records.Add(new Record { Key = "still-valid", ExpiresAt = now.AddDays(7) });
        await db.SaveChangesAsync();

        var removed = await db.Records.CleanupExpiredIdempotencyRecordsWithLoggingAsync(
            now, NullLogger.Instance, "Test", CancellationToken.None);

        Assert.Equal(0, removed);
        Assert.Single(await db.Records.ToListAsync());
    }
}
