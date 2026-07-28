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

    public async Task<IReadOnlyList<EmployeeAudienceDetail>> GetEmployeeAudienceDetailsAsync(
        Guid companyId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken)
    {
        var employees = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.DepartmentId, e.LocationId, e.ManagerId })
            .ToListAsync(cancellationToken);

        var departmentNames = await dbContext.Departments.AsNoTracking()
            .Where(d => d.CompanyId == companyId)
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var locationNames = await dbContext.Locations.AsNoTracking()
            .Where(l => l.CompanyId == companyId)
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);

        var managerIds = employees.Where(e => e.ManagerId.HasValue).Select(e => e.ManagerId!.Value).Distinct().ToList();

        var managerNames = await dbContext.Employees.AsNoTracking()
            .Where(e => e.CompanyId == companyId && managerIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id, e => $"{e.FirstName} {e.LastName}".Trim(), cancellationToken);

        return employees.Select(e => new EmployeeAudienceDetail(
            e.Id,
            e.DepartmentId,
            departmentNames.TryGetValue(e.DepartmentId, out var departmentName) ? departmentName : null,
            e.LocationId,
            locationNames.TryGetValue(e.LocationId, out var locationName) ? locationName : null,
            e.ManagerId,
            e.ManagerId is { } managerId && managerNames.TryGetValue(managerId, out var managerName) ? managerName : null)).ToList();
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

    public async Task<IReadOnlyList<Guid>> GetAllEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
        await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
}
