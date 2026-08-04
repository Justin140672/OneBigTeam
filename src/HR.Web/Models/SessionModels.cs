using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Web.Models;

public sealed record MeResponse(
    Guid UserId,
    Guid CompanyId,
    string? Email,
    List<Guid> PermissionIds,
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
    string? ProfileImageUrl);
