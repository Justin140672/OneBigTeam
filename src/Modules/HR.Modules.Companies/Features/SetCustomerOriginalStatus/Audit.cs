using HR.SharedKernel;

namespace HR.Modules.Companies.Features.SetCustomerOriginalStatus;

internal sealed record CustomerClassificationUpdatedAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    bool OldIsOriginalCustomer,
    bool NewIsOriginalCustomer) : IAuditEvent
{
    string IAuditEvent.EventType => "subscription.customer-classification-updated";
    string IAuditEvent.EntityType => "CustomerSubscription";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Customer classification updated from {OldIsOriginalCustomer} to {NewIsOriginalCustomer}";
    object? IAuditEvent.Before => new { IsOriginalCustomer = OldIsOriginalCustomer };
    object? IAuditEvent.After => new { IsOriginalCustomer = NewIsOriginalCustomer };
    object? IAuditEvent.Metadata => null;
}
