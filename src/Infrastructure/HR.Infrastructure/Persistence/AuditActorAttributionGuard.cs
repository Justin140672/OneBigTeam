using HR.SharedKernel;

namespace HR.Infrastructure.Persistence;

/// <summary>
/// AUD-04: validates that human-triggered audit events supply a resolvable actor identity.
///
/// Background and integration-handler events are explicitly classified as non-human via
/// <see cref="AuditActorType"/>; those events are not required to carry a user actor ID.
///
/// Throws <see cref="MissingAuditActorException"/> when a Human event has neither
/// <see cref="IAuditEvent.ActorUserId"/> nor <see cref="IAuditEvent.ActorEmployeeId"/> set,
/// so it is always clear in the audit log whether an action was taken by a person or a system
/// process.
/// </summary>
internal static class AuditActorAttributionGuard
{
    public static void Assert(IAuditEvent evt)
    {
        if (evt.ActorType != AuditActorType.Human)
            return;

        if (evt.ActorUserId.HasValue || evt.ActorEmployeeId.HasValue)
            return;

        throw new MissingAuditActorException(
            $"AUD-04: human-triggered audit event '{evt.EventType}' (entity {evt.EntityType}:{evt.EntityId}) " +
            $"does not supply ActorUserId or ActorEmployeeId. " +
            $"Set the actor from the current user, or mark the event as a ScheduledJob/IntegrationHandler.");
    }
}

/// <summary>Thrown when a Human audit event carries no actor identity.</summary>
public sealed class MissingAuditActorException(string message) : InvalidOperationException(message);
