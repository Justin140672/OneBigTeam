namespace HR.Modules.Employees.Features.CreatePositionProfile;

internal sealed record CreatePositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    bool IsManagerial,
    int? ProbationMonthsOverride,
    bool IsActive,
    DateTimeOffset CreatedAt);
