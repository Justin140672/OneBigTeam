using HR.SharedKernel.ExecutionContext;

namespace HR.Modules.Companies.Domain;

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

    // Ticket 23 (P2): durable correlation metadata, stamped from the ambient execution context at
    // creation time - nullable so rows written before this migration remain fully usable.
    public Guid? CorrelationId { get; private set; }
    public Guid? CausationId { get; private set; }
    public Guid? MessageId { get; private set; }

    public static OutboxMessage CreatePending(
        Guid id,
        Guid companyId,
        string eventType,
        string payload,
        DateTimeOffset createdAt,
        IExecutionContext? executionContext = null)
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
            CorrelationId = executionContext is null ? null : CorrelationIdGuid.Derive(executionContext.CorrelationId),
            CausationId = executionContext?.MessageId,
            MessageId = Guid.NewGuid(),
        };
    }

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

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        Status = StatusFailed;
        FailedAt = now;
        ErrorMessage = reason;
    }

    public void ResetForRetry(DateTimeOffset now)
    {
        if (Status != StatusFailed)
            throw new InvalidOperationException($"Cannot retry an outbox message with status '{Status}'.");

        Status = StatusPending;
        FailedAt = null;
        ErrorMessage = null;
    }
}
