using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

// Published whenever an in-progress leaving process is cancelled (CancelLeavingProcessHandler).
// Consuming modules (e.g. Leave, LEAVE-05) use this to restore the employee's current policy year
// entitlement to the figure it would have been had they never entered the leaving process (i.e.
// recalculate with no leaving date at all), while leaving any usage recorded or manual adjustment
// made during the leaving-pending period untouched.
public sealed record EmployeeLeavingProcessCancelledIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
