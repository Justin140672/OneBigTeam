using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Jobs;

/// <summary>
/// Core, per-company TOIL expiry logic (LEAVE-06). For every active, expiry-configured TOIL leave
/// type (<see cref="LeaveType.ToilExpiryDays"/> set), finds every Earned bucket whose
/// <see cref="ToilTransaction.ExpiresOn"/> has passed and still has a remaining, unexpired amount,
/// and writes an Expired ledger transaction for exactly that remaining amount - making it
/// permanently unavailable to future FIFO consumption (see ToilLedgerService, which already
/// excludes expired buckets and nets off Expired transactions when computing bucket remainders).
///
/// Idempotent by design, mirroring <see cref="LeaveYearRolloverService"/>: a bucket that already
/// has an Expired transaction recorded against it (RelatedTransactionId) is skipped, so re-running
/// this for the same company/day is a safe no-op.
/// </summary>
internal sealed class ToilExpiryService(LeaveDbContext dbContext, IClock clock, IAuditEventPublisher auditPublisher)
{
    internal static readonly Guid SystemActorId = Guid.Empty;

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

        var dueBuckets = await dbContext.ToilTransactions
            .Where(t => t.CompanyId == companyId
                     && balanceIds.Contains(t.LeaveBalanceId)
                     && t.Type == ToilTransactionType.Earned
                     && t.ExpiresOn != null
                     && t.ExpiresOn <= asOf)
            .ToListAsync(cancellationToken);

        if (dueBuckets.Count == 0)
            return ToilExpiryResult.Empty;

        var bucketIds = dueBuckets.Select(b => b.Id).ToList();
        var drawdowns = await dbContext.ToilTransactions
            .Where(t => t.CompanyId == companyId
                     && t.RelatedTransactionId != null
                     && bucketIds.Contains(t.RelatedTransactionId!.Value))
            .ToListAsync(cancellationToken);

        // Idempotency guard: a bucket that already has an Expired transaction against it has
        // already been processed.
        var alreadyExpiredBucketIds = drawdowns
            .Where(d => d.Type == ToilTransactionType.Expired)
            .Select(d => d.RelatedTransactionId!.Value)
            .ToHashSet();

        var now = clock.UtcNowOffset();
        var expiredTransactions = new List<ToilTransaction>();

        var balances = await dbContext.LeaveBalances
            .Where(b => balanceIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

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
            return ToilExpiryResult.Empty;

        dbContext.ToilTransactions.AddRange(expiredTransactions);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
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
}

internal sealed record ToilExpiryResult(int TransactionsCreated)
{
    public static ToilExpiryResult Empty { get; } = new(0);
}
