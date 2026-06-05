namespace HR.Modules.Companies.Domain;

internal sealed class OutboxMessage
{
    private OutboxMessage() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static OutboxMessage Create(
        Guid id,
        Guid companyId,
        string eventType,
        string payload,
        DateTimeOffset now)
    {
        return new OutboxMessage
        {
            Id = id,
            CompanyId = companyId,
            EventType = eventType,
            Payload = payload,
            Status = "pending",
            CreatedAt = now,
            ProcessedAt = null,
        };
    }

    public void MarkProcessed(DateTimeOffset processedAt)
    {
        Status = "processed";
        ProcessedAt = processedAt;
    }
}
