namespace HR.SharedKernel;

/// <summary>
/// AUD-04: classifies the origin of an audit event so the publisher can validate that
/// human-triggered operations supply an actor and background operations are explicitly
/// acknowledged as system-originated.
/// </summary>
public enum AuditActorType
{
    /// <summary>
    /// An authenticated user or employee took the action directly.
    /// Either <see cref="IAuditEvent.ActorUserId"/> or <see cref="IAuditEvent.ActorEmployeeId"/>
    /// must be non-null when this type is used — the publisher will reject the event otherwise.
    /// </summary>
    Human = 0,

    /// <summary>
    /// A scheduled background job or Hangfire recurring task triggered the operation.
    /// No user actor is expected; a recognisable job identifier should appear in the Summary.
    /// </summary>
    ScheduledJob = 1,

    /// <summary>
    /// An integration event handler processed a cross-module event.
    /// No user actor is expected; the originating event/correlation context identifies the source.
    /// </summary>
    IntegrationHandler = 2,

    /// <summary>
    /// A platform support session action taken on behalf of a tenant.
    /// Both the support actor and the tenant context should be identifiable via the event payload.
    /// </summary>
    SupportSession = 3,
}
