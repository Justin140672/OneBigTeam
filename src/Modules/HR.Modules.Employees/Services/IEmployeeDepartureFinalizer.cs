using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Services;

internal interface IEmployeeDepartureFinalizer
{
    Task FinalizeAsync(
        Employee employee,
        EmployeeLeavingProcess process,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
