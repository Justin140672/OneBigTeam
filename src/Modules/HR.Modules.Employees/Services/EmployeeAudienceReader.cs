using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeAudienceReader(EmployeesDbContext dbContext) : IEmployeeAudienceReader
{
    public async Task<EmployeeAudienceProfile?> GetEmployeeAudienceAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Id == employeeId)
            .Select(e => new { e.DepartmentId, e.LocationId, e.PositionProfileId })
            .FirstOrDefaultAsync(cancellationToken);

        return employee is null
            ? null
            : new EmployeeAudienceProfile(employee.DepartmentId, employee.LocationId, employee.PositionProfileId);
    }

    public Task<bool> DepartmentExistsAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
        dbContext.Departments.AsNoTracking()
            .AnyAsync(d => d.Id == departmentId && d.CompanyId == companyId, cancellationToken);

    public Task<bool> LocationExistsAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken) =>
        dbContext.Locations.AsNoTracking()
            .AnyAsync(l => l.Id == locationId && l.CompanyId == companyId, cancellationToken);

    public Task<bool> PositionProfileExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        dbContext.PositionProfiles.AsNoTracking()
            .AnyAsync(p => p.Id == positionProfileId && p.CompanyId == companyId, cancellationToken);

    public Task<bool> EmployeeExistsAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken) =>
        dbContext.Employees.AsNoTracking()
            .AnyAsync(e => e.Id == employeeId && e.CompanyId == companyId, cancellationToken);

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

    public Task<string?> GetPositionProfileNameAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        dbContext.PositionProfiles.AsNoTracking()
            .Where(p => p.Id == positionProfileId && p.CompanyId == companyId)
            .Select(p => p.Title)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetEligibleEmployeeIdsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> departmentIds,
        IReadOnlyCollection<Guid> locationIds,
        IReadOnlyCollection<Guid> positionProfileIds,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Status == EmploymentStatus.Active);

        var hasRules = departmentIds.Count > 0 || locationIds.Count > 0 || positionProfileIds.Count > 0 || employeeIds.Count > 0;

        if (hasRules)
        {
            query = query.Where(e =>
                departmentIds.Contains(e.DepartmentId) ||
                locationIds.Contains(e.LocationId) ||
                positionProfileIds.Contains(e.PositionProfileId) ||
                employeeIds.Contains(e.Id));
        }

        return await query.Select(e => e.Id).ToListAsync(cancellationToken);
    }
}
