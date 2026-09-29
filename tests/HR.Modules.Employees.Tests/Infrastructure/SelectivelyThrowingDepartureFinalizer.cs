using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;

namespace HR.Modules.Employees.Tests.Infrastructure;

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
