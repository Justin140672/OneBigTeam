using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Web.Models;

public sealed record MeResponse(
    Guid UserId,
    Guid CompanyId,
    string? Email,
    List<Guid> PermissionIds,
    List<Guid> RoleIds,
    bool CanManageCompany,
    bool IsHrAdministrator,
    bool IsManager,
    bool IsRecruiter,
    bool IsEmailConfirmed);

public sealed record MyEmployeeResponse(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string? JobTitle,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    string? ProfileImageUrl,
    bool RequiresInitialSetup);
