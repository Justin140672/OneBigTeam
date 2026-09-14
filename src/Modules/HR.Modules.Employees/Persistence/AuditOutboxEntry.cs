using HR.SharedKernel.Outbox;

namespace HR.Modules.Employees.Persistence;

/// <summary>
/// This module's own audit/integration-outbox entity (ticket 3, P1 follow-up item 5). Kept internal
/// to this assembly - see <see cref="IAuditOutboxEntry"/> for why a shared public entity type isn't
/// used.
/// </summary>
internal sealed class AuditOutboxEntry : IAuditOutboxEntry
{
    public Guid Id { get; set; }
    public string Channel { get; set; } = OutboxChannel.Audit;
    public string EventTypeName { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public bool IsTerminallyFailed { get; set; }
}
