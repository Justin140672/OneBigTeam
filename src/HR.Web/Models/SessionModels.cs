using HR.SharedKernel;

namespace HR.Web.Models;

public sealed record MeResponse(
    Guid UserId,
    Guid CompanyId,
    string? Email,
    List<Guid> PermissionIds);

public sealed record MyEmployeeResponse(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string? JobTitle,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    string? ProfileImageUrl);
