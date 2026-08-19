using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

// Deliberately carries NO salary/amount field — timeline entries derived from this event must
// never surface a compensation figure (see EmployeeTimelineVisibility's redaction rule in
// HR.Modules.Employees.Domain).
public sealed record CompensationChangedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationId,
    DateOnly EffectiveFrom,
    string SalaryType,
    string Reason,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
