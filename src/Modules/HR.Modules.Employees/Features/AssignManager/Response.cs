namespace HR.Modules.Employees.Features.AssignManager;

internal sealed record AssignManagerResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ManagerId,
    string? ManagerFullName,
    DateTimeOffset UpdatedAt);
