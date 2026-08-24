using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

/// <summary>
/// Owns the TOIL ledger's FIFO consumption, cancellation-reversal and expiry algorithms
/// (LEAVE-06). Every TOIL balance change is represented as a typed <see cref="ToilTransaction"/>;
/// this service is the single place that writes Used/Adjusted(reversal)/Expired rows so the
/// ledger stays internally consistent and the aggregate <see cref="LeaveBalance"/> row (kept for
/// display/reporting) is updated in lockstep with the ledger, in the same unit of work.
///
/// "Bucket" = an Earned transaction. Its remaining amount is its own Days minus everything drawn
/// against it (Used/Expired transactions whose RelatedTransactionId points at it) plus anything
/// reversed back into it (Adjusted reversal transactions whose RelatedTransactionId points at it).
/// Buckets are consumed oldest-earned-first (FIFO), skipping buckets that have already expired.
///
/// This is a module-owned domain service (mirrors <c>LeaveYearRolloverService</c>), not a generic
/// repository - it operates entirely through the module's own <see cref="LeaveDbContext"/> and is
/// invoked from feature handlers (ApproveLeaveRequest, CancelLeaveRequest) and the TOIL expiry job.
/// </summary>
internal sealed class ToilLedgerService(LeaveDbContext dbContext)
{
    /// <summary>
    /// Consumes <paramref name="amountDays"/> of TOIL for <paramref name="employeeId"/>, walking
    /// non-expired Earned buckets oldest-first and splitting across as many buckets as necessary.
    /// Fails with a validation error if the employee's available TOIL is insufficient and
    /// <paramref name="allowNegativeBalance"/> is false; when true and insufficient, the shortfall
    /// is drawn from the single oldest bucket (or a synthetic overdraw if there are no buckets at
    /// all) so the ledger still records where the deficit came from.
    /// </summary>
    public async Task<Result<ToilConsumptionResult>> ConsumeAsync(
        Guid companyId,
        Guid employeeId,
        Guid leaveTypeId,
        decimal amountDays,
        Guid sourceLeaveRequestId,
        Guid actorEmployeeId,
        DateOnly occurredOn,
        bool allowNegativeBalance,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var balances = await dbContext.LeaveBalances
            .Where(b => b.CompanyId == companyId && b.EmployeeId == employeeId && b.LeaveTypeId == leaveTypeId)
            .ToListAsync(cancellationToken);

        if (balances.Count == 0)
            return Result.Failure<ToilConsumptionResult>(
                Error.Validation("No TOIL balance found for this employee. The request cannot be approved."));

        var balanceById = balances.ToDictionary(b => b.Id);

        var buckets = await GetOpenBucketsOrderedAsync(companyId, employeeId, leaveTypeId, occurredOn, cancellationToken);

        var available = buckets.Sum(b => b.Remaining);

        if (available < amountDays && !allowNegativeBalance)
            return Result.Failure<ToilConsumptionResult>(
                Error.Validation(
                    $"Insufficient TOIL balance: {available} day(s) available, {amountDays} day(s) requested."));

        var remainingToConsume = amountDays;
        var created = new List<ToilTransaction>();

        foreach (var bucket in buckets)
        {
            if (remainingToConsume <= 0)
                break;

            var takeFromBucket = Math.Min(bucket.Remaining, remainingToConsume);
            if (takeFromBucket <= 0)
                continue;

            var used = ToilTransaction.CreateUsed(
                Guid.NewGuid(),
                companyId,
                employeeId,
                bucket.Transaction.LeaveBalanceId,
                bucket.Transaction.Id,
                sourceLeaveRequestId,
                actorEmployeeId,
                takeFromBucket,
                occurredOn,
                $"TOIL used against award of {bucket.Transaction.OccurredOn:d MMM yyyy}",
                now);

            created.Add(used);
            balanceById[bucket.Transaction.LeaveBalanceId].RecordUsage(takeFromBucket, now);
            remainingToConsume -= takeFromBucket;
        }

        // Allowed overdraw beyond every known bucket - record it against the oldest balance row
        // (or the only one available) so it is still visible in that balance's history, even
        // though there is no specific Earned bucket left to attribute it to.
        if (remainingToConsume > 0)
        {
            var fallbackBalance = balances.OrderBy(b => b.PolicyYear).First();
            var overdraw = ToilTransaction.CreateUsed(
                Guid.NewGuid(),
                companyId,
                employeeId,
                fallbackBalance.Id,
                bucketTransactionId: null, // no source bucket - overdraw beyond every known award
                sourceLeaveRequestId,
                actorEmployeeId,
                remainingToConsume,
                occurredOn,
                "TOIL used beyond available balance (policy permits negative TOIL balance)",
                now);

            created.Add(overdraw);
            fallbackBalance.RecordUsage(remainingToConsume, now);
        }

        dbContext.ToilTransactions.AddRange(created);

        return Result.Success(new ToilConsumptionResult(created));
    }

    /// <summary>
    /// Reverses every Used transaction previously recorded against <paramref name="leaveRequestId"/>
    /// that has not already been reversed, restoring the correct buckets so future FIFO consumption
    /// remains correct. Idempotent: transactions that already have a matching reversal
    /// (ReversesTransactionId) are skipped, so calling this twice for the same request is a no-op
    /// on the second call.
    /// </summary>
    public async Task<ToilReversalResult> ReverseAsync(
        Guid companyId,
        Guid employeeId,
        Guid leaveRequestId,
        Guid actorEmployeeId,
        DateOnly occurredOn,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var usedTransactions = await dbContext.ToilTransactions
            .Where(t => t.CompanyId == companyId
                     && t.EmployeeId == employeeId
                     && t.SourceLeaveRequestId == leaveRequestId
                     && t.Type == ToilTransactionType.Used)
            .ToListAsync(cancellationToken);

        if (usedTransactions.Count == 0)
            return ToilReversalResult.Empty;

        var alreadyReversedIds = await dbContext.ToilTransactions
            .Where(t => t.CompanyId == companyId
                     && t.EmployeeId == employeeId
                     && t.Type == ToilTransactionType.Adjusted
                     && t.ReversesTransactionId != null
                     && usedTransactions.Select(u => u.Id).Contains(t.ReversesTransactionId!.Value))
            .Select(t => t.ReversesTransactionId!.Value)
            .ToListAsync(cancellationToken);

        var toReverse = usedTransactions.Where(u => !alreadyReversedIds.Contains(u.Id)).ToList();
        if (toReverse.Count == 0)
            return ToilReversalResult.Empty;

        var balanceIds = toReverse.Select(t => t.LeaveBalanceId).Distinct().ToList();
        var balances = await dbContext.LeaveBalances
            .Where(b => balanceIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var reversals = new List<ToilTransaction>();
        foreach (var used in toReverse)
        {
            var reversal = ToilTransaction.CreateReversal(
                Guid.NewGuid(),
                companyId,
                employeeId,
                used.LeaveBalanceId,
                used.RelatedTransactionId ?? Guid.Empty,
                used.Id,
                leaveRequestId,
                actorEmployeeId,
                used.Days,
                occurredOn,
                "TOIL usage reversed (leave request cancelled)",
                now);

            reversals.Add(reversal);

            if (balances.TryGetValue(used.LeaveBalanceId, out var balance))
                balance.ReverseUsage(used.Days, now);
        }

        dbContext.ToilTransactions.AddRange(reversals);

        return new ToilReversalResult(reversals);
    }

    private async Task<List<ToilBucket>> GetOpenBucketsOrderedAsync(
        Guid companyId,
        Guid employeeId,
        Guid leaveTypeId,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var balanceIds = await dbContext.LeaveBalances
            .Where(b => b.CompanyId == companyId && b.EmployeeId == employeeId && b.LeaveTypeId == leaveTypeId)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        if (balanceIds.Count == 0)
            return [];

        var earned = await dbContext.ToilTransactions
            .Where(t => t.CompanyId == companyId
                     && t.EmployeeId == employeeId
                     && balanceIds.Contains(t.LeaveBalanceId)
                     && t.Type == ToilTransactionType.Earned)
            .OrderBy(t => t.OccurredOn)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        if (earned.Count == 0)
            return [];

        var earnedIds = earned.Select(e => e.Id).ToList();
        var drawdowns = await dbContext.ToilTransactions
            .Where(t => t.CompanyId == companyId
                     && t.EmployeeId == employeeId
                     && t.RelatedTransactionId != null
                     && earnedIds.Contains(t.RelatedTransactionId!.Value))
            .ToListAsync(cancellationToken);

        var buckets = new List<ToilBucket>();
        foreach (var bucket in earned)
        {
            // Expired buckets are never available for further consumption.
            if (bucket.ExpiresOn.HasValue && bucket.ExpiresOn.Value <= asOf)
                continue;

            var consumed = drawdowns.Where(d => d.RelatedTransactionId == bucket.Id && d.Type == ToilTransactionType.Used).Sum(d => d.Days);
            var reversed = drawdowns.Where(d => d.RelatedTransactionId == bucket.Id && d.Type == ToilTransactionType.Adjusted).Sum(d => d.Days);
            var expired = drawdowns.Where(d => d.RelatedTransactionId == bucket.Id && d.Type == ToilTransactionType.Expired).Sum(d => d.Days);

            var remaining = bucket.Days - consumed - expired + reversed;
            if (remaining > 0)
                buckets.Add(new ToilBucket(bucket, remaining));
        }

        return buckets;
    }
}

internal sealed record ToilBucket(ToilTransaction Transaction, decimal Remaining);

internal sealed record ToilConsumptionResult(IReadOnlyList<ToilTransaction> Transactions);

internal sealed record ToilReversalResult(IReadOnlyList<ToilTransaction> Transactions)
{
    public static ToilReversalResult Empty { get; } = new([]);
}
