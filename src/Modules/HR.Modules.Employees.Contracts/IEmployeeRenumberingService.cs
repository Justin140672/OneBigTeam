namespace HR.Modules.Employees.Contracts;

/// <summary>
/// Cross-module write surface used exclusively by HR.Modules.Companies' UpdateHrSettings handler
/// to renumber every existing employee in a company after the employee-number FORMAT changes
/// while the company stays in Automatic numbering mode. This is a fresh, purpose-built mechanism
/// (deliberately not a reuse of the removed employee-number "backfill" feature). Implemented in
/// HR.Modules.Employees.Services and DI-registered in EmployeesModule.
/// </summary>
public interface IEmployeeRenumberingService
{
    Task RenumberAllEmployeesAsync(Guid companyId, CancellationToken cancellationToken);
}
