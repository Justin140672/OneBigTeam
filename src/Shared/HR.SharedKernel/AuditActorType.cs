namespace HR.SharedKernel;

public enum AuditActorType
{
    /// <summary>
    /// An authenticated user or employee took the action directly.
    /// Either <see cref="IAuditEvent.ActorUserId"/> or <see cref="IAuditEvent.ActorEmployeeId"/>
    /// must be non-null when this type is used — the publisher will reject the event otherwise.
    /// </summary>
    Human = 0,

    ScheduledJob = 1,

    IntegrationHandler = 2,

    SupportSession = 3,

    Anonymous = 4,
}
