using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HR.Modules.Leave.Jobs;

/// <summary>
/// Core, per-company TOIL expiry logic (LEAVE-06). For every active, expiry-configured TOIL leave
/// type (<see cref="LeaveType.ToilExpiryDays"/> set), finds every Earned bucket whose
/// <see cref="ToilTransaction.ExpiresOn"/> has passed and still has a remaining, unexpired amount,
/// and writes an Expired ledger transaction for exactly that remaining amount - making it
/// permanently unavailable to future FIFO consumption (see ToilLedgerService, which already
/// excludes expired buckets and nets off Expired transactions when computing bucket remainders).
///
/// Concurrency (P1 follow-up to Ticket 4, hardened as a P1.1 follow-up): the concurrency boundary
/// is established BEFORE the ledger is read, not after. This method opens a transaction and takes
/// a row lock (<c>SELECT ... FOR UPDATE</c>) on every candidate leave balance first, and the row
/// lock query IS the query that materializes the tracked balance entities used for the rest of the
/// calculation (see <see cref="LoadLockedBalancesAsync"/>) - there is no separate, earlier,
/// unlocked load of those same entities. An earlier version of this method loaded the balances
/// first and only acquired the lock afterwards; that ordering let a concurrent writer commit a
/// change to a balance between the load and the lock, leaving the calculation working from a
/// stale tracked version that then failed to save with <see cref="DbUpdateConcurrencyException"/>.
/// Ledger reads (due buckets + drawdowns) happen after the locked load, inside that same
/// transaction. Because Postgres row locks also block ordinary UPDATEs against the locked rows,
/// this serializes against:
///   - a concurrent, overlapping run of this same method (e.g. a retried/duplicated Hangfire
///     execution) - the second run blocks until the first commits, then re-reads fresh ledger data
///     rather than acting on a stale snapshot taken before the first run's writes.
///   - concurrent TOIL usage/reversal (ToilLedgerService.ConsumeAsync/ReverseAsync), which also
///     mutates the same LeaveBalance row - its UPDATE blocks until this transaction releases the
///     lock, and vice versa.
///
/// A DB-enforced unique, filtered index (ix_toil_transactions_related_transaction_id_expired_unique
/// - see ToilTransactionConfiguration) additionally guarantees at most one Expired transaction can
/// ever exist per bucket, as a defence-in-depth backstop against double-expiry even if the row lock
/// is ever bypassed (e.g. a future caller forgetting to route through this method). A unique
/// violation on that index is treated as "already expired by someone else" - a safe no-op, exactly
/// like the in-memory idempotency guard below.
///
/// Defence in depth: despite the row lock, <see cref="ToilExpiryJob"/> additionally wraps each
/// company's call to this method in a small bounded retry loop, discarding all state and starting
/// from a fresh scope (fresh <see cref="LeaveDbContext"/>/transaction) on
/// <see cref="DbUpdateConcurrencyException"/> - see that class's doc comment for the rationale.
/// </summary>
internal sealed class ToilExpiryService(LeaveDbContext dbContext, IClock clock, IAuditEventPublisher auditPublisher)
{
    internal static readonly Guid SystemActorId = Guid.Empty;

    /// <summary>
    /// Test-only extensibility point (P1.1 follow-up). When set, invoked exactly once per
    /// <see cref="ExpireCompanyAsync"/> call, immediately after the locked balances have been
    /// loaded (<see cref="LoadLockedBalancesAsync"/>) but before the ledger is read/calculated -
    /// i.e. squarely inside the concurrency boundary described in this class's doc comment. This
    /// lets Postgres-backed integration tests either (a) pause the method here (e.g. via a
    /// <see cref="TaskCompletionSource"/>) so a concurrent caller can commit a change in that
    /// window, or (b) deterministically force a stale-concurrency-token save failure by mutating
    /// the loaded entities' tracked original values. Never set outside tests; defaults to null and
    /// is a no-op for every real caller (constructor-injected via DI, which never sets it).
    /// </summary>
    internal Func<LeaveDbContext, Dictionary<Guid, LeaveBalance>, CancellationToken, Task>? TestOnlyAfterLockedLoadAsync { get; set; }

    public async Task<ToilExpiryResult> ExpireCompanyAsync(Guid companyId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var toilLeaveTypes = await dbContext.LeaveTypes
            .Where(lt => lt.CompanyId == companyId
                      && lt.Behaviour == LeaveTypeBehaviour.Toil
                      && lt.IsActive
                      && lt.ToilExpiryDays != null)
            .ToListAsync(cancellationToken);

        if (toilLeaveTypes.Count == 0)
            return ToilExpiryResult.Empty;

        var toilLeaveTypeIds = toilLeaveTypes.Select(lt => lt.Id).ToList();

        var balanceIds = await dbContext.LeaveBalances
            .Where(b => b.CompanyId == companyId && toilLeaveTypeIds.Contains(b.LeaveTypeId))
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        if (balanceIds.Count == 0)
            return ToilExpiryResult.Empty;

        // Concurrency boundary starts here, BEFORE any ledger read used for the expiry calculation.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Row-lock every candidate balance up front, AND materialize the tracked balance entities
        // used for the rest of this calculation from that same locked read - never from an earlier,
        // separately-loaded snapshot. Any concurrent writer touching one of these balance rows
        // (another expiry run, or TOIL usage/reversal) blocks until this transaction commits or
        // rolls back, so both the balances in memory and the ledger reads below are guaranteed
        // consistent with whatever that writer eventually persists - never a stale interleaving.
        var balances = await LoadLockedBalancesAsync(balanceIds, cancellationToken);

        if (TestOnlyAfterLockedLoadAsync is not null)
            await TestOnlyAfterLockedLoadAsync(dbContext, balances, cancellationToken);

        var dueBuckets = await dbContext.ToilTransactions
            .Where(t => t.CompanyId == companyId
                     && balanceIds.Contains(t.LeaveBalanceId)
                     && t.Type == ToilTransactionType.Earned
                     && t.ExpiresOn != null
                     && t.ExpiresOn <= asOf)
            .ToListAsync(cancellationToken);

        if (dueBuckets.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToilExpiryResult.Empty;
        }

        var bucketIds = dueBuckets.Select(b => b.Id).ToList();
        var drawdowns = await dbContext.ToilTransactions
            .Where(t => t.CompanyId == companyId
                     && t.RelatedTransactionId != null
                     && bucketIds.Contains(t.RelatedTransactionId!.Value))
            .ToListAsync(cancellationToken);

        // Idempotency guard: a bucket that already has an Expired transaction against it has
        // already been processed. Backed by the unique filtered index below as the authoritative
        // guard; this in-memory check just avoids the round trip to discover that on the common path.
        var alreadyExpiredBucketIds = drawdowns
            .Where(d => d.Type == ToilTransactionType.Expired)
            .Select(d => d.RelatedTransactionId!.Value)
            .ToHashSet();

        var now = clock.UtcNowOffset();
        var expiredTransactions = new List<ToilTransaction>();

        foreach (var bucket in dueBuckets)
        {
            if (alreadyExpiredBucketIds.Contains(bucket.Id))
                continue;

            var used = drawdowns.Where(d => d.RelatedTransactionId == bucket.Id && d.Type == ToilTransactionType.Used).Sum(d => d.Days);
            var reversed = drawdowns.Where(d => d.RelatedTransactionId == bucket.Id && d.Type == ToilTransactionType.Adjusted).Sum(d => d.Days);

            var remaining = bucket.Days - used + reversed;
            if (remaining <= 0)
                continue;

            var expired = ToilTransaction.CreateExpired(
                Guid.NewGuid(),
                companyId,
                bucket.EmployeeId,
                bucket.LeaveBalanceId,
                bucket.Id,
                SystemActorId,
                remaining,
                asOf,
                $"TOIL expired: award of {bucket.OccurredOn:d MMM yyyy}",
                now);

            expiredTransactions.Add(expired);

            if (balances.TryGetValue(bucket.LeaveBalanceId, out var balance))
                balance.Adjust(-remaining, now);
        }

        if (expiredTransactions.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToilExpiryResult.Empty;
        }

        dbContext.ToilTransactions.AddRange(expiredTransactions);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ix_toil_transactions_related_transaction_id_expired_unique"))
        {
            // Backstop: another writer expired at least one of these buckets between our idempotency
            // check and this save (should not happen given the row lock above, but the constraint is
            // the authoritative guard, so treat it as a safe no-op rather than corrupting the balance
            // with a partially-applied expiry).
            await transaction.RollbackAsync(cancellationToken);
            return ToilExpiryResult.Empty;
        }

        await transaction.CommitAsync(cancellationToken);

        foreach (var expired in expiredTransactions)
        {
            await auditPublisher.PublishAsync(new ToilExpiredAuditEvent(
                expired.CompanyId,
                expired.EmployeeId,
                expired.Id,
                expired.LeaveBalanceId,
                expired.RelatedTransactionId!.Value,
                expired.Days,
                expired.OccurredOn,
                now), cancellationToken);
        }

        return new ToilExpiryResult(expiredTransactions.Count);
    }

    /// <summary>
    /// Loads the tracked <see cref="LeaveBalance"/> entities used for the rest of
    /// <see cref="ExpireCompanyAsync"/>'s calculation. On relational providers, the row lock IS the
    /// query that produces these tracked entities (<c>SELECT ... FOR UPDATE</c> via
    /// <c>FromSqlInterpolated</c>), so there is no window between "read the balance" and "lock the
    /// balance" during which a concurrent writer could commit a change this method would miss.
    /// </summary>
    private async Task<Dictionary<Guid, LeaveBalance>> LoadLockedBalancesAsync(
        List<Guid> balanceIds, CancellationToken cancellationToken)
    {
        // Only relational providers (Postgres in production, Sqlite/Postgres in integration tests)
        // support raw SQL / row locking. Unit tests exercise this service against EF's InMemory
        // provider, which has no concept of row locks - correctness of the concurrency boundary
        // itself is covered by the Postgres-backed integration tests, not these unit tests.
        if (!dbContext.Database.IsRelational())
        {
            return await dbContext.LeaveBalances
                .Where(b => balanceIds.Contains(b.Id))
                .OrderBy(b => b.Id) // stable order avoids lock-ordering deadlocks with ToilLedgerService
                .ToDictionaryAsync(b => b.Id, cancellationToken);
        }

        return await dbContext.LeaveBalances
            .FromSqlInterpolated(
                $"SELECT * FROM leave.leave_balances WHERE id = ANY({balanceIds}) ORDER BY id FOR UPDATE")
            .ToDictionaryAsync(b => b.Id, cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException ex, string constraintOrIndexName) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pgEx
        && pgEx.ConstraintName == constraintOrIndexName;
}

internal sealed record ToilExpiryResult(int TransactionsCreated)
{
    public static ToilExpiryResult Empty { get; } = new(0);
}
