using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeDirectoryReader(EmployeesDbContext dbContext) : IEmployeeDirectoryReader
{
    public async Task<PagedResult<EmployeeDirectoryReportItem>> GetEmployeeDirectoryAsync(
        Guid companyId,
        ReportFilterCriteria filter,
        Pagination pagination,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId);

        if (filter.DepartmentId is not null)
            query = query.Where(e => e.DepartmentId == filter.DepartmentId);

        if (filter.LocationId is not null)
            query = query.Where(e => e.LocationId == filter.LocationId);

        if (filter.PositionProfileId is not null)
            query = query.Where(e => e.PositionProfileId == filter.PositionProfileId);

        if (filter.ManagerId is not null)
            query = query.Where(e => e.ManagerId == filter.ManagerId);

        if (filter.EmploymentTypeId is not null)
            query = query.Where(e => e.EmploymentTypeId == filter.EmploymentTypeId);

        if (!string.IsNullOrWhiteSpace(filter.EmployeeStatus) &&
            Enum.TryParse<EmploymentStatus>(filter.EmployeeStatus, ignoreCase: true, out var status))
        {
            query = query.Where(e => e.Status == status);
        }

        if (filter.DateRangeStart is not null)
            query = query.Where(e => e.StartDate >= filter.DateRangeStart);

        if (filter.DateRangeEnd is not null)
            query = query.Where(e => e.StartDate <= filter.DateRangeEnd);

        query = sortBy?.ToLowerInvariant() switch
        {
            "employeenumber" => sortDescending ? query.OrderByDescending(e => e.EmployeeNumber) : query.OrderBy(e => e.EmployeeNumber),
            "startdate" => sortDescending ? query.OrderByDescending(e => e.StartDate) : query.OrderBy(e => e.StartDate),
            "status" => sortDescending ? query.OrderByDescending(e => e.Status) : query.OrderBy(e => e.Status),
            "name" or _ => sortDescending
                ? query.OrderByDescending(e => e.LastName).ThenByDescending(e => e.FirstName)
                : query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName),
        };

        var totalCount = await query.CountAsync(cancellationToken);

        var employees = await query
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        var departmentIds = employees.Select(e => e.DepartmentId).ToHashSet();
        var positionProfileIds = employees.Select(e => e.PositionProfileId).ToHashSet();
        var locationIds = employees.Select(e => e.LocationId).ToHashSet();
        var employmentTypeIds = employees.Select(e => e.EmploymentTypeId).ToHashSet();
        var managerIds = employees
            .Where(e => e.ManagerId is not null)
            .Select(e => e.ManagerId!.Value)
            .ToHashSet();

        var departmentNames = departmentIds.Count > 0
            ? await dbContext.Departments
                .AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var positionProfileTitles = positionProfileIds.Count > 0
            ? await dbContext.PositionProfiles
                .AsNoTracking()
                .Where(p => positionProfileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken)
            : new Dictionary<Guid, string>();

        var locationNames = locationIds.Count > 0
            ? await dbContext.Locations
                .AsNoTracking()
                .Where(l => locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var employmentTypeNames = employmentTypeIds.Count > 0
            ? await dbContext.EmploymentTypes
                .AsNoTracking()
                .Where(t => employmentTypeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var managerNames = managerIds.Count > 0
            ? await dbContext.Employees
                .AsNoTracking()
                .Where(e => managerIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => $"{e.FirstName} {e.LastName}", cancellationToken)
            : new Dictionary<Guid, string>();

        var items = employees
            .Select(e => new EmployeeDirectoryReportItem(
                e.Id,
                e.EmployeeNumber,
                $"{e.FirstName} {e.LastName}",
                departmentNames.TryGetValue(e.DepartmentId, out var deptName) ? deptName : null,
                positionProfileTitles.TryGetValue(e.PositionProfileId, out var posTitle) ? posTitle : null,
                e.ManagerId is not null && managerNames.TryGetValue(e.ManagerId.Value, out var mgrName) ? mgrName : null,
                employmentTypeNames.TryGetValue(e.EmploymentTypeId, out var etName) ? etName : null,
                e.StartDate,
                e.Status.ToString(),
                locationNames.TryGetValue(e.LocationId, out var locName) ? locName : null,
                e.WorkEmail))
            .ToList();

        return new PagedResult<EmployeeDirectoryReportItem>(items, totalCount, pagination.PageNumber, pagination.PageSize);
    }
}
