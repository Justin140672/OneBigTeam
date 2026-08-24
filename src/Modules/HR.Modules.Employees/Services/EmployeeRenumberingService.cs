using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

/// <summary>
/// Implements <see cref="IEmployeeRenumberingService"/>. A fresh, purpose-built mechanism for
/// renumbering every employee in a company after its employee-number FORMAT changes while staying
/// in Automatic mode (item 27) — deliberately not a reuse of the removed
/// Preview/CommitBackfillEmployeeNumbers feature.
/// </summary>
internal sealed class EmployeeRenumberingService(
    EmployeesDbContext dbContext,
    IEmployeeNumberGenerator employeeNumberGenerator) : IEmployeeRenumberingService
{
    public async Task RenumberAllEmployeesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        // Deterministic order so a re-run (e.g. after a partial failure) renumbers employees in
        // the same sequence — oldest employee gets the lowest new number.
        var employees = await dbContext.Employees
            .Where(e => e.CompanyId == companyId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var employee in employees)
        {
            // No exceptions: every employee is renumbered to the new format, including ones whose
            // current number was manually entered and doesn't match any pattern.
            var newNumber = await employeeNumberGenerator.GenerateNextAsync(companyId, cancellationToken);
            employee.SetEmployeeNumber(newNumber);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
