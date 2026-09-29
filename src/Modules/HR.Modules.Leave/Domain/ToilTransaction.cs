namespace HR.Modules.Leave.Domain;

internal sealed class ToilTransaction
{
    private ToilTransaction() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid LeaveBalanceId { get; private set; }
    public ToilTransactionType Type { get; private set; }

    public decimal Days { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>
    /// For Used/Expired/reversal-Adjusted transactions: the id of the Earned bucket this
    /// transaction draws from or expires. Null for Earned transactions and for standalone manual
    /// Adjusted corrections that are not tied to a specific bucket.
    /// </summary>
    public Guid? RelatedTransactionId { get; private set; }

    public Guid? ReversesTransactionId { get; private set; }

    public Guid? SourceLeaveRequestId { get; private set; }

    public Guid ActorEmployeeId { get; private set; }

    public string? Notes { get; private set; }

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
