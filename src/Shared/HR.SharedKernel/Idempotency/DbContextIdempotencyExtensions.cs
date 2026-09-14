using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Ticket 3 (P1) follow-up base code: lets a handler dedupe a client-supplied "Idempotency-Key"
/// against the same DbContext/transaction as its business write, so the key and the result commit
/// atomically with the change (or neither does). Mirrors the "insert, catch the 23505 from a
/// concurrent duplicate, replay the winner" pattern already used by <c>TaskCreator</c> and
/// <c>NotificationWriter</c>, generalised for reuse across modules via the shared kernel.
///
/// Generic over each module's own <see cref="IIdempotencyRecord"/> implementation (rather than a
/// single shared entity type) so every module's EF entity CLR type stays internal to that module's
/// assembly - see <see cref="IIdempotencyRecord"/> for why.
/// </summary>
public static class DbContextIdempotencyExtensions
{
    private const string UniqueViolationSqlState = "23505";

    /// <summary>
    /// Default retention window: comfortably longer than any legitimate client retry (a user
    /// re-clicking "Try again", a page rerender resubmitting a pending request, or the HTTP client's
    /// own bounded retry budget - see HR.ServiceDefaults' TotalRequestTimeout, currently 120s) can
    /// plausibly span. 7 days covers a user closing their laptop mid-request and resuming the next
    /// working day, while still bounding how long a stored response body is retained.
    /// </summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(7);

    /// <summary>
    /// Deterministic fingerprint of a request payload, used to detect a key being reused for a
    /// materially different request rather than a genuine retry of the same one.
    /// </summary>
    public static string Fingerprint(object request)
    {
        var json = JsonSerializer.Serialize(request);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Looks for a previously completed request under <paramref name="scope"/> + <paramref name="idempotencyKey"/>.
    /// Call this before doing any business-entity work so a duplicate delivery short-circuits without
    /// re-running side effects (e.g. publishing another audit event).
    /// Returns <c>null</c> when the key hasn't been used before in this scope - the caller should
    /// proceed as normal. A key that WAS used, but under a different scope, is indistinguishable from
    /// one never used at all - by design, so one company/actor/operation can never discover or
    /// replay another's stored result.
    /// </summary>
    public static async Task<IdempotencyOutcome<TResponse>?> TryReplayAsync<TRecord, TResponse>(
        this DbContext dbContext,
        IdempotencyScope scope,
        string idempotencyKey,
        string requestFingerprint,
        CancellationToken cancellationToken)
        where TRecord : class, IIdempotencyRecord =>
        await ReplayAsync<TRecord, TResponse>(dbContext, scope, idempotencyKey, requestFingerprint, cancellationToken);

    /// <summary>
    /// Stages a <typeparamref name="TRecord"/> for <paramref name="scope"/> + <paramref name="idempotencyKey"/>
    /// and saves it in the SAME SaveChangesAsync call as whatever business-entity changes the caller
    /// has already added to <paramref name="dbContext"/>'s change tracker (not yet saved). Either both
    /// commit together, or - if a concurrent duplicate under the same scope+key won the race - neither
    /// does, and the winner's stored result is returned instead.
    /// </summary>
    public static async Task<IdempotencyOutcome<TResponse>> SaveIdempotentAsync<TRecord, TResponse>(
        this DbContext dbContext,
        DbSet<TRecord> records,
        IdempotencyScope scope,
        string idempotencyKey,
        string requestFingerprint,
        int statusCode,
        TResponse response,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        TimeSpan? retention = null)
        where TRecord : class, IIdempotencyRecord, new()
    {
        // Ticket 3 (P1) follow-up item 5: refuse to persist a response shape that looks like it
        // carries secrets, before it's ever serialized.
        ReplayResponsePolicy.EnsureReplaySafe<TResponse>();

        var record = new TRecord
        {
            OperationId = scope.OperationId,
            CompanyId = scope.CompanyId,
            ActorId = scope.ActorId,
            Key = idempotencyKey,
            RequestFingerprint = requestFingerprint,
            ResponseStatusCode = statusCode,
            ResponseBodyJson = JsonSerializer.Serialize(response),
            CreatedAt = now,
            ExpiresAt = now + (retention ?? DefaultRetention),
        };

        records.Add(record);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return IdempotencyOutcome<TResponse>.Completed(statusCode, response);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Postgres aborts the whole transaction on any error - if the caller opened one
            // explicitly (BeginTransactionAsync), it must be rolled back before this DbContext can
            // run another query, or that query fails with "current transaction is aborted". The
            // caller must not Commit afterwards; disposing an already-rolled-back transaction is a
            // harmless no-op.
            if (dbContext.Database.CurrentTransaction is not null)
                await dbContext.Database.RollbackTransactionAsync(cancellationToken);

            // None of this attempt's business rows were persisted either. Detach everything staged
            // so the (shared, scoped) DbContext stays safe to reuse, then replay the concurrent
            // winner's stored result.
            foreach (var entry in dbContext.ChangeTracker.Entries().ToList())
                entry.State = EntityState.Detached;

            return await ReplayAsync<TRecord, TResponse>(dbContext, scope, idempotencyKey, requestFingerprint, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Idempotency key '{idempotencyKey}' raced a concurrent insert but no record could be found afterwards.");
        }
    }

    /// <summary>
    /// Ticket 3 (P1) follow-up item 6: deletes one bounded batch of expired records and returns how
    /// many were removed. Safe under concurrent execution - this issues a single DELETE statement (no
    /// read-then-act race), and only ever matches rows already past <see cref="IIdempotencyRecord.ExpiresAt"/>,
    /// which by construction only exist for completed (never partial/in-flight) operations - a record
    /// is only ever created together with the business save it protects, in <see cref="SaveIdempotentAsync{TRecord,TResponse}"/>.
    /// Call this repeatedly (e.g. from a recurring background job) until it returns 0.
    /// </summary>
    public static async Task<int> CleanupExpiredIdempotencyRecordsAsync<TRecord>(
        this DbSet<TRecord> records,
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken)
        where TRecord : class, IIdempotencyRecord
    {
        // Select the batch's composite keys first (a plain ordered SELECT+LIMIT - supported by
        // every provider), then delete by an OR of exact-key matches in one statement. Deliberately
        // NOT `.Where(...).OrderBy(...).Take(n).ExecuteDeleteAsync()` - that combination isn't
        // portable across EF providers (SQLite's provider rejects it outright; whatever Npgsql
        // supports here isn't something this environment can verify against a live Postgres), while
        // a plain equality OR is guaranteed to translate everywhere.
        var batch = await records
            .Where(r => r.ExpiresAt <= now)
            .OrderBy(r => r.ExpiresAt)
            .Select(r => new { r.OperationId, r.CompanyId, r.ActorId, r.Key })
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
            return 0;

        var parameter = Expression.Parameter(typeof(TRecord), "r");
        Expression? predicate = null;

        foreach (var key in batch)
        {
            Expression clause = Expression.AndAlso(
                Expression.AndAlso(
                    Expression.Equal(
                        Expression.Property(parameter, nameof(IIdempotencyRecord.OperationId)),
                        Expression.Constant(key.OperationId)),
                    Expression.Equal(
                        Expression.Property(parameter, nameof(IIdempotencyRecord.CompanyId)),
                        Expression.Constant(key.CompanyId))),
                Expression.AndAlso(
                    Expression.Equal(
                        Expression.Property(parameter, nameof(IIdempotencyRecord.ActorId)),
                        Expression.Constant(key.ActorId)),
                    Expression.Equal(
                        Expression.Property(parameter, nameof(IIdempotencyRecord.Key)),
                        Expression.Constant(key.Key))));

            predicate = predicate is null ? clause : Expression.OrElse(predicate, clause);
        }

        var lambda = Expression.Lambda<Func<TRecord, bool>>(predicate!, parameter);
        return await records.Where(lambda).ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<IdempotencyOutcome<TResponse>?> ReplayAsync<TRecord, TResponse>(
        DbContext dbContext,
        IdempotencyScope scope,
        string idempotencyKey,
        string requestFingerprint,
        CancellationToken cancellationToken)
        where TRecord : class, IIdempotencyRecord
    {
        var existing = await dbContext.Set<TRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.OperationId == scope.OperationId
                  && r.CompanyId == scope.CompanyId
                  && r.ActorId == scope.ActorId
                  && r.Key == idempotencyKey,
                cancellationToken);

        if (existing is null)
            return null;

        if (existing.RequestFingerprint != requestFingerprint)
            return IdempotencyOutcome<TResponse>.KeyReused();

        var response = JsonSerializer.Deserialize<TResponse>(existing.ResponseBodyJson)!;
        return IdempotencyOutcome<TResponse>.Replayed(existing.ResponseStatusCode, response);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is { } inner
        && inner.GetType().Name == "PostgresException"
        && inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == UniqueViolationSqlState;
}
