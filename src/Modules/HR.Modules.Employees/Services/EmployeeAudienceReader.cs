using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeAudienceReader(EmployeesDbContext dbContext) : IEmployeeAudienceReader
{
    public async Task<(Guid? DepartmentId, Guid? LocationId)> GetEmployeeAudienceAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Id == employeeId)
            .Select(e => new { e.DepartmentId, e.LocationId })
            .FirstOrDefaultAsync(cancellationToken);

        return employee is null ? (null, null) : (employee.DepartmentId, employee.LocationId);
    }

    public Task<bool> DepartmentExistsAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
        dbContext.Departments.AsNoTracking()
            .AnyAsync(d => d.Id == departmentId && d.CompanyId == companyId, cancellationToken);

    public Task<bool> LocationExistsAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken) =>
        dbContext.Locations.AsNoTracking()
            .AnyAsync(l => l.Id == locationId && l.CompanyId == companyId, cancellationToken);

    public Task<string?> GetDepartmentNameAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
        dbContext.Departments.AsNoTracking()
            .Where(d => d.Id == departmentId && d.CompanyId == companyId)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<string?> GetLocationNameAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken) =>
        dbContext.Locations.AsNoTracking()
            .Where(l => l.Id == locationId && l.CompanyId == companyId)
            .Select(l => l.Name)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetEligibleEmployeeIdsAsync(
        Guid companyId, Guid? departmentId, Guid? locationId, CancellationToken cancellationToken)
    {
        var query = dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Status == EmploymentStatus.Active);

        if (departmentId is not null)
            query = query.Where(e => e.DepartmentId == departmentId);

        if (locationId is not null)
            query = query.Where(e => e.LocationId == locationId);

        return await query.Select(e => e.Id).ToListAsync(cancellationToken);
    }
}
