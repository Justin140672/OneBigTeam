using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

// IAM-03: published whenever a position profile is created, updated, or deactivated. Not currently
// consumed by anything (HR.Modules.Identity's Position projection is kept in sync via lazy pull —
// see PositionSync — rather than subscribing to this event), but published anyway as the
// module-owned signal of record for this state change, matching the platform's "publish when the
// workflow changes state" convention (see 04-event-architecture.md) and leaving room for a future
// consumer (e.g. a Reporting projection) without another Employees-side change.
public sealed record PositionProfileUpsertedIntegrationEvent(
    Guid CompanyId,
    Guid PositionProfileId,
    string Title,
    bool IsActive,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
