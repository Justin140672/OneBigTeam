using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Services;

// The single idempotent finalisation operation referenced by the backdated-leaving-date product
// decision: "a Leaving Date before today ... immediately finalises the employee through the
// standard idempotent leaving-finalisation operation." ProcessLeavingEmployeesJob and
// Start/AmendLeavingProcessHandler (when HR confirms a backdated LeavingDate) both call this same
// path so an employee's departure is always finalised the same way regardless of trigger.
internal interface IEmployeeDepartureFinalizer
{
    Task FinalizeAsync(
        Employee employee,
        EmployeeLeavingProcess process,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
