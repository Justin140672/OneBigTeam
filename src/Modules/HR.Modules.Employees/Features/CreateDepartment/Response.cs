namespace HR.Modules.Employees.Features.CreateDepartment;

internal sealed record CreateDepartmentResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid? ParentDepartmentId,
    bool IsActive,
    DateTimeOffset CreatedAt);
