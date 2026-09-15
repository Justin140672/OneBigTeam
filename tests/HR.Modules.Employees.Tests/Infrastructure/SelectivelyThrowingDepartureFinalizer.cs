using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;

namespace HR.Modules.Employees.Tests.Infrastructure;

/// <summary>
/// Wraps a real IEmployeeDepartureFinalizer but throws for a configured set of employee ids —
/// lets ProcessLeavingEmployeesJobTests simulate one employee's finalisation permanently failing
/// while proving the rest of the batch (and the stranded-recovery scan) still runs.
/// </summary>
internal sealed class SelectivelyThrowingDepartureFinalizer(
    IEmployeeDepartureFinalizer inner, params Guid[] throwingEmployeeIds) : IEmployeeDepartureFinalizer
{
    private readonly HashSet<Guid> _throwingEmployeeIds = [.. throwingEmployeeIds];
    public List<Guid> InvokedFor { get; } = [];

    public Task FinalizeAsync(
        Employee employee, EmployeeLeavingProcess process, DateTimeOffset now, CancellationToken cancellationToken)
    {
        InvokedFor.Add(employee.Id);

        if (_throwingEmployeeIds.Contains(employee.Id))
            throw new InvalidOperationException($"Simulated permanent failure for employee {employee.Id}.");

        return inner.FinalizeAsync(employee, process, now, cancellationToken);
    }
}
