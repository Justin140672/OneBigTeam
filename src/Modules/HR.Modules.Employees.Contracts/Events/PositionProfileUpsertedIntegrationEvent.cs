using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

public sealed record PositionProfileUpsertedIntegrationEvent(
    Guid CompanyId,
    Guid PositionProfileId,
    string Title,
    bool IsActive,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
