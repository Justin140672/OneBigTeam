using HR.Modules.Employees.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.UpdateEmploymentDetails;

internal sealed record UpdateEmploymentDetailsResponse(
    Guid Id,
    Guid CompanyId,
    string? EmployeeNumber,
    Guid? EmploymentTypeId,
    EmploymentStatus Status,
    Guid? DepartmentId,
    Guid? LocationId,
    Guid? PositionProfileId,
    Guid? ManagerId,
    DateOnly StartDate,
    DateOnly? ContinuousServiceDate,
    DateOnly? ProbationEndDate,
    DateOnly? LeavingDate,
    NoticePeriodUnit? NoticePeriodUnitOverride,
    int? NoticePeriodLengthOverride,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    string? Notes,
    DateTimeOffset UpdatedAt);
