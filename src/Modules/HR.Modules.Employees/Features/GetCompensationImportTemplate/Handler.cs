using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetCompensationImportTemplate;

internal sealed class GetCompensationImportTemplateHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader timeZoneReader)
{
    public async Task<byte[]> GenerateAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var today = await CompanyToday.ResolveAsync(companyId, clock, timeZoneReader, cancellationToken);

        var employees = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Status != EmploymentStatus.FormerEmployee)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName })
            .ToListAsync(cancellationToken);

        var employeeIds = employees.Select(e => e.Id).ToList();

        var currentCompensations = await dbContext.Compensations
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && employeeIds.Contains(c.EmployeeId) &&
                        c.EffectiveFrom <= today && (c.EffectiveTo == null || c.EffectiveTo >= today))
            .ToListAsync(cancellationToken);

        var currentByEmployee = currentCompensations
            .GroupBy(c => c.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(c => c.EffectiveFrom).ThenByDescending(c => c.CreatedAt).First());

        var rows = employees
            .Select(e =>
            {
                currentByEmployee.TryGetValue(e.Id, out var current);
                return new CompensationImportTemplateRow(
                    e.EmployeeNumber,
                    $"{e.FirstName} {e.LastName}",
                    current?.Salary,
                    current?.SalaryType.ToString());
            })
            .ToList();

        return CompensationImportTemplateBuilder.Build(rows);
    }
}
