using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeImportLookupReader(EmployeesDbContext dbContext) : IEmployeeImportLookupReader
{
    public async Task<bool> EmployeeNumberExistsAsync(Guid companyId, string employeeNumber, CancellationToken cancellationToken)
    {
        var normalized = employeeNumber.Trim();

        return await dbContext.Employees
            .AsNoTracking()
            .AnyAsync(
                e => e.CompanyId == companyId &&
                     e.EmployeeNumber != null &&
                     e.EmployeeNumber.ToLower() == normalized.ToLower(),
                cancellationToken);
    }

    public async Task<bool> WorkEmailExistsAsync(Guid companyId, string workEmail, CancellationToken cancellationToken)
    {
        var normalized = workEmail.Trim().ToLowerInvariant();

        return await dbContext.Employees
            .AsNoTracking()
            .AnyAsync(
                e => e.CompanyId == companyId && e.WorkEmail == normalized,
                cancellationToken);
    }

    public async Task<Guid?> FindEmployeeIdByReferenceAsync(Guid companyId, string reference, CancellationToken cancellationToken)
    {
        var normalized = reference.Trim().ToLowerInvariant();

        var employee = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId &&
                        ((e.EmployeeNumber != null && e.EmployeeNumber.ToLower() == normalized) ||
                         e.WorkEmail == normalized))
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return employee;
    }

    public async Task<Guid?> TryFindInitialCompanyAdminEmployeeIdByWorkEmailAsync(
        Guid companyId, string workEmail, CancellationToken cancellationToken)
    {
        var normalized = workEmail.Trim().ToLowerInvariant();

        return await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.WorkEmail == normalized && e.IsInitialCompanyAdmin)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
