using HR.SharedKernel;

namespace HR.Modules.Employees.Features.UpdateEmployeeProfile;

internal sealed record UpdateEmployeeProfileRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? PositionProfileId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PreferredName { get; init; }
    public string WorkEmail { get; init; } = string.Empty;
    public string? PersonalEmail { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? Nationality { get; init; }
    public string? Gender { get; init; }
    public string? GenderOther { get; init; }
    public bool HasSystemAccess { get; init; } = true;
    public WorkingDays? WorkingDaysOverride { get; init; }
    public decimal? HoursPerDayOverride { get; init; }
}
