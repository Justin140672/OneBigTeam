namespace HR.SharedKernel.Outbox;

/// <summary>Which publisher redelivers an <see cref="IAuditOutboxEntry"/>. See that type's doc comment.</summary>
public static class OutboxChannel
{
    public const string Audit = "audit";
    public const string Integration = "integration";
}
