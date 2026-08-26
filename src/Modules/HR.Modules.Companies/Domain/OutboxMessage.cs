namespace HR.Modules.Companies.Domain;

/// <summary>
/// A durable, module-scoped outbox record for a business-side-effect instruction that must be
/// processed asynchronously and reliably after the owning transaction commits (see
/// specifications/architecture/05-database-standards.md "Optional Outbox Persistence").
///
/// SET-08: extended beyond the original bare pending/processed shape with a full
/// Pending -&gt; Processing -&gt; Processed|Failed state machine, attempt tracking and a captured error
/// message, so a numbering-format-change side effect (see EmployeeRenumberSideEffectJob) is:
///   - created in the same transaction as the settings change that requested it (durability),
///   - safely retryable (MarkProcessing/MarkFailed never lose the row — Failed is a visible,
///     detectable state a support/HR action can retry, not silent data loss),
///   - never left ambiguously "maybe applied, maybe not" after a crash — the row's Status is always
///     one of the four states below, so recovery tooling/monitoring can always tell which.
/// </summary>
internal sealed class OutboxMessage
{
    private OutboxMessage() { }

    public const string StatusPending    = "pending";
    public const string StatusProcessing = "processing";
    public const string StatusProcessed  = "processed";
    public const string StatusFailed     = "failed";

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }

    public static OutboxMessage CreatePending(
        Guid id,
        Guid companyId,
        string eventType,
        string payload,
        DateTimeOffset createdAt)
    {
        return new OutboxMessage
        {
            Id = id,
            CompanyId = companyId,
            EventType = eventType,
            Payload = payload,
            Status = StatusPending,
            AttemptCount = 0,
            CreatedAt = createdAt,
        };
    }

    /// <summary>Records that a real processing attempt is starting. Safe to call again after a
    /// crash mid-attempt or on a manual retry of a Failed row — always overwrites to Processing.</summary>
    public void MarkProcessing(DateTimeOffset now)
    {
        Status = StatusProcessing;
        AttemptCount++;
        LastAttemptAt = now;
        ErrorMessage = null;
    }

    public void MarkProcessed(DateTimeOffset processedAt)
    {
        Status = StatusProcessed;
        ProcessedAt = processedAt;
        ErrorMessage = null;
    }

    /// <summary>
    /// Marks this instruction as failed — a final, visible, detectable state (never a silently
    /// stuck "processing" row) that a caller can inspect and explicitly retry via
    /// RetryEmployeeRenumberSideEffect, which resets Status back to Pending and clears FailedAt.
    /// </summary>
    public void MarkFailed(string reason, DateTimeOffset now)
    {
        Status = StatusFailed;
        FailedAt = now;
        ErrorMessage = reason;
    }

    /// <summary>Resets a Failed instruction back to Pending so it can be re-enqueued and retried.
    /// Throws if called on anything other than a Failed row.</summary>
    public void ResetForRetry(DateTimeOffset now)
    {
        if (Status != StatusFailed)
            throw new InvalidOperationException($"Cannot retry an outbox message with status '{Status}'.");

        Status = StatusPending;
        FailedAt = null;
        ErrorMessage = null;
    }
}
