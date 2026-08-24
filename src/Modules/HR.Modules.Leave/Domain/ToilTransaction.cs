namespace HR.Modules.Leave.Domain;

/// <summary>
/// A single typed entry in the TOIL ledger (LEAVE-06). The ledger is the source of truth for TOIL
/// balances - every award, consumption, reversal and expiry is represented as one of these rows,
/// each with an actor, a date, an amount (always stored positive; direction is implied by
/// <see cref="Type"/>) and a traceable source.
///
/// An Earned transaction is itself a FIFO "bucket": <see cref="Days"/> is the amount originally
/// awarded, and its remaining balance is the amount not yet accounted for by Used/Expired
/// transactions (and any Adjusted reversals) whose <see cref="RelatedTransactionId"/> points back
/// at it - see <c>ToilLedgerService</c> for the consumption/reversal/expiry algorithms.
/// </summary>
internal sealed class ToilTransaction
{
    private ToilTransaction() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid LeaveBalanceId { get; private set; }
    public ToilTransactionType Type { get; private set; }

    /// <summary>Always positive; the magnitude of the change. Direction is implied by <see cref="Type"/>.</summary>
    public decimal Days { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    /// <summary>
    /// Only set on Earned transactions (buckets) when the company's TOIL policy configures an
    /// expiry (see <see cref="LeaveType.ToilExpiryDays"/>). Null means this bucket never expires.
    /// </summary>
    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>
    /// For Used/Expired/reversal-Adjusted transactions: the id of the Earned bucket this
    /// transaction draws from or expires. Null for Earned transactions and for standalone manual
    /// Adjusted corrections that are not tied to a specific bucket.
    /// </summary>
    public Guid? RelatedTransactionId { get; private set; }

    /// <summary>Only set on a reversal Adjusted transaction: the specific Used transaction it reverses.</summary>
    public Guid? ReversesTransactionId { get; private set; }

    /// <summary>The leave request that caused this Used or reversal transaction, if any.</summary>
    public Guid? SourceLeaveRequestId { get; private set; }

    /// <summary>The person or system actor responsible for this ledger entry.</summary>
    public Guid ActorEmployeeId { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Human-meaningful description for balance history display.</summary>
    public string Description { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ToilTransaction CreateEarned(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid leaveBalanceId,
        Guid awardedByEmployeeId,
        decimal days,
        DateOnly occurredOn,
        DateOnly? expiresOn,
        string? notes,
        DateTimeOffset now)
    {
        return new ToilTransaction
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveBalanceId = leaveBalanceId,
            Type = ToilTransactionType.Earned,
            Days = days,
            OccurredOn = occurredOn,
            ExpiresOn = expiresOn,
            ActorEmployeeId = awardedByEmployeeId,
            Notes = notes,
            Description = "TOIL awarded" + (notes is null ? "" : $": {notes}"),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static ToilTransaction CreateUsed(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid leaveBalanceId,
        Guid? bucketTransactionId,
        Guid sourceLeaveRequestId,
        Guid actorEmployeeId,
        decimal days,
        DateOnly occurredOn,
        string description,
        DateTimeOffset now)
    {
        return new ToilTransaction
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveBalanceId = leaveBalanceId,
            Type = ToilTransactionType.Used,
            Days = days,
            OccurredOn = occurredOn,
            RelatedTransactionId = bucketTransactionId,
            SourceLeaveRequestId = sourceLeaveRequestId,
            ActorEmployeeId = actorEmployeeId,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static ToilTransaction CreateReversal(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid leaveBalanceId,
        Guid bucketTransactionId,
        Guid reversedUsedTransactionId,
        Guid sourceLeaveRequestId,
        Guid actorEmployeeId,
        decimal days,
        DateOnly occurredOn,
        string description,
        DateTimeOffset now)
    {
        return new ToilTransaction
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveBalanceId = leaveBalanceId,
            Type = ToilTransactionType.Adjusted,
            Days = days,
            OccurredOn = occurredOn,
            RelatedTransactionId = bucketTransactionId,
            ReversesTransactionId = reversedUsedTransactionId,
            SourceLeaveRequestId = sourceLeaveRequestId,
            ActorEmployeeId = actorEmployeeId,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static ToilTransaction CreateExpired(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid leaveBalanceId,
        Guid bucketTransactionId,
        Guid actorEmployeeId,
        decimal days,
        DateOnly occurredOn,
        string description,
        DateTimeOffset now)
    {
        return new ToilTransaction
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveBalanceId = leaveBalanceId,
            Type = ToilTransactionType.Expired,
            Days = days,
            OccurredOn = occurredOn,
            RelatedTransactionId = bucketTransactionId,
            ActorEmployeeId = actorEmployeeId,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static ToilTransaction CreateManualAdjustment(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid leaveBalanceId,
        Guid actorEmployeeId,
        decimal days,
        DateOnly occurredOn,
        string description,
        DateTimeOffset now)
    {
        return new ToilTransaction
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveBalanceId = leaveBalanceId,
            Type = ToilTransactionType.Adjusted,
            Days = days,
            OccurredOn = occurredOn,
            ActorEmployeeId = actorEmployeeId,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
