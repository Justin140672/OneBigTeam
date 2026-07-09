namespace HR.SharedKernel;
public sealed record EmployeeCreatedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly StartDate,
    Guid? ManagerId,
    DateOnly ProbationEndDate,
    Guid? PositionProfileId = null,
    Guid? DefaultLeavePolicyId = null,
    bool IsImported = false) : IIntegrationEvent;
