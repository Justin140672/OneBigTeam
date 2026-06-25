namespace HR.Modules.Employees.Features.UpdatePositionProfile;

internal sealed record UpdatePositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    bool IsManagerial,
    int? ProbationMonthsOverride,
    bool IsActive,
    DateTimeOffset UpdatedAt);
