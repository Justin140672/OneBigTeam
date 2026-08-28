using System.Text.Json;
using HR.SharedKernel;

namespace HR.Infrastructure.Persistence;

internal sealed class AuditEvent
{
    private AuditEvent() { }

    public Guid Id { get; private set; }
    /// <summary>AUD-01: stable idempotency key from <see cref="IAuditEvent.EventId"/>. Unique.</summary>
    public Guid EventId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public Guid? EmployeeId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public Guid? ActorEmployeeId { get; private set; }
    /// <summary>AUD-04: origin classification (Human, ScheduledJob, IntegrationHandler, SupportSession).</summary>
    public AuditActorType ActorType { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public string? Summary { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public string? MetadataJson { get; private set; }

    internal static AuditEvent From(IAuditEvent evt) => new()
    {
        Id            = Guid.NewGuid(),
        EventId       = evt.EventId,
        CompanyId     = evt.CompanyId,
        EventType     = evt.EventType,
        EntityType    = evt.EntityType,
        EntityId      = evt.EntityId,
        EmployeeId    = evt.EmployeeId,
        ActorUserId     = evt.ActorUserId,
        ActorEmployeeId = evt.ActorEmployeeId,
        ActorType       = evt.ActorType,
        OccurredAt      = evt.OccurredAt,
        CorrelationId   = evt.CorrelationId,
        Summary         = evt.Summary,
        BeforeJson    = evt.Before   is null ? null : JsonSerializer.Serialize(evt.Before),
        AfterJson     = evt.After    is null ? null : JsonSerializer.Serialize(evt.After),
        MetadataJson  = evt.Metadata is null ? null : JsonSerializer.Serialize(evt.Metadata),
    };

    /// <summary>AUD-01: promotes a <see cref="PendingAuditPayload"/> from the pending staging table.</summary>
    internal static AuditEvent FromPayload(PendingAuditPayload p) => new()
    {
        Id              = Guid.NewGuid(),
        EventId         = p.EventId,
        CompanyId       = p.CompanyId,
        EventType       = p.EventType,
        EntityType      = p.EntityType,
        EntityId        = p.EntityId,
        EmployeeId      = p.EmployeeId,
        ActorUserId     = p.ActorUserId,
        ActorEmployeeId = p.ActorEmployeeId,
        ActorType       = p.ActorType,
        OccurredAt      = p.OccurredAt,
        CorrelationId   = p.CorrelationId,
        Summary         = p.Summary,
        BeforeJson      = p.BeforeJson,
        AfterJson       = p.AfterJson,
        MetadataJson    = p.MetadataJson,
    };
}
