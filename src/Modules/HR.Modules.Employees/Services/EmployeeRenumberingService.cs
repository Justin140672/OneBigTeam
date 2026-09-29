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
        var employees = await dbContext.Employees
            .Where(e => e.CompanyId == companyId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var employee in employees)
        {
            var newNumber = await employeeNumberGenerator.GenerateNextAsync(companyId, cancellationToken);
            employee.SetEmployeeNumber(newNumber);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
