using HR.Modules.Employees.Contracts;

namespace HR.Modules.Recruitment.Domain;

internal sealed record OfferTermsSnapshot(
    Guid? PositionProfileId,
    string? JobTitle,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    Guid? EmploymentTypeId,
    string? EmploymentTypeName,
    Guid? ProposedManagerId,
    string? ProposedManagerName,
    bool NoManager,
    string? Currency,
    WorkingDays? WorkingDays,
    decimal? HoursPerDay,
    decimal? HoursPerWeek,
    decimal? Fte,
    int? ProbationMonths,
    DateOnly? ResponseDeadline);
