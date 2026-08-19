using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

public sealed record EmployeePromotedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid PreviousPositionProfileId,
    Guid NewPositionProfileId,
    DateOnly EffectiveDate,
    Guid PromotionId) : IIntegrationEvent;
