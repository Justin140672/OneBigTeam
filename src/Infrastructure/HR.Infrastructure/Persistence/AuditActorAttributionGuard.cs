using HR.SharedKernel;

namespace HR.Infrastructure.Persistence;

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

public sealed class MissingAuditActorException(string message) : InvalidOperationException(message);
