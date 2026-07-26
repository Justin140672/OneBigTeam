namespace HR.Modules.Employees.Features.PromoteEmployee;

internal sealed record PromoteEmployeeResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid PreviousPositionProfileId,
    Guid NewPositionProfileId,
    Guid? NewManagerId,
    Guid? NewLocationId,
    DateOnly EffectiveDate,
    string Reason,
    string? Notes,
    Guid? CompensationId,
    DateTimeOffset CreatedDate,
    DateTimeOffset? CompletedAt);
