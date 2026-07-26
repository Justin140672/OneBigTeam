using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.PromoteEmployee;

internal sealed record PromoteEmployeeRequest(
    Guid CompanyId,
    Guid EmployeeId,
    Guid NewPositionProfileId,
    DateOnly EffectiveDate,
    string Reason,
    string? Notes,
    Guid? NewManagerId,
    Guid? NewLocationId,
    bool ConfirmBackdatedEffectiveDate = false,
    bool CreateCompensationChange = false,
    SalaryType? CompensationSalaryType = null,
    decimal? CompensationSalary = null,
    string? CompensationCurrency = null,
    decimal? CompensationHoursPerWeek = null,
    decimal? CompensationFte = null,
    string? CompensationNotes = null);
