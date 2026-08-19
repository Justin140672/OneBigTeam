using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

// Deliberately carries no field-level detail — the timeline entry derived from this event must
// stay generic ("Employee details updated") rather than naming which personal fields changed, to
// avoid leaking sensitive personal-detail changes (e.g. personal email, DOB) into a broadly
// visible timeline entry. See EmployeeTimelineVisibility for the full visibility rules.
public sealed record EmployeeDetailsCorrectedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
