using HR.SharedKernel;

namespace HR.Modules.Companies.Features.ExecuteCustomerDeletion;

/// <summary>
/// Records a platform-administrator manually executing a pending permanent deletion. See
/// CustomerSubscription.ExecuteDeletion for the explicit scope line: this is a status-only,
/// reversible-in-principle transition (revokes access via AdminForcedReadOnly), NOT irreversible
/// hard-deletion of the company's actual data. The audit summary intentionally reflects that scope
/// so it reads correctly on the Platform Audit Log.
/// </summary>
internal sealed record CustomerDeletionExecutedAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Reason) : IAuditEvent
{
    string IAuditEvent.EventType => "subscription.deletion-executed";
    string IAuditEvent.EntityType => "CustomerSubscription";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary =>
        $"Permanent deletion executed (status marked deleted, access revoked; underlying data " +
        $"retained pending a dedicated hard-deletion process). Reason: {Reason}";
    object? IAuditEvent.Before => new { DeletionExecutedAt = (DateTimeOffset?)null };
    object? IAuditEvent.After => new { DeletionExecutedAt = OccurredAt };
    object? IAuditEvent.Metadata => new { Reason };
}
