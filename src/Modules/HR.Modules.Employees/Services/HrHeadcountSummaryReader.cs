using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class HrHeadcountSummaryReader(EmployeesDbContext dbContext) : IHrHeadcountSummaryReader
{
    public async Task<HrHeadcountSummaryResult> GetHeadcountSummaryAsync(
        Guid companyId,
        ReportFilterCriteria filter,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var query = dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId);

        if (filter.DepartmentId is not null)
            query = query.Where(e => e.DepartmentId == filter.DepartmentId);

        if (filter.LocationId is not null)
            query = query.Where(e => e.LocationId == filter.LocationId);

        if (filter.EmploymentTypeId is not null)
            query = query.Where(e => e.EmploymentTypeId == filter.EmploymentTypeId);

        if (!string.IsNullOrWhiteSpace(filter.EmployeeStatus) &&
            Enum.TryParse<EmploymentStatus>(filter.EmployeeStatus, ignoreCase: true, out var status))
        {
            query = query.Where(e => e.Status == status);
        }

        var employees = await query
            .Select(e => new
            {
                e.Id,
                e.FirstName,
                e.LastName,
                e.DepartmentId,
                e.LocationId,
                e.PositionProfileId,
                e.EmploymentTypeId,
                e.Status,
                e.StartDate,
                e.LeavingDate,
            })
            .ToListAsync(cancellationToken);

        var departmentIds = employees.Select(e => e.DepartmentId).ToHashSet();
        var locationIds = employees.Select(e => e.LocationId).ToHashSet();
        var positionProfileIds = employees.Select(e => e.PositionProfileId).ToHashSet();
        var employmentTypeIds = employees.Select(e => e.EmploymentTypeId).ToHashSet();
        var employeeIds = employees.Select(e => e.Id).ToHashSet();

        var departmentNames = departmentIds.Count > 0
            ? await dbContext.Departments
                .AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var locationNames = locationIds.Count > 0
            ? await dbContext.Locations
                .AsNoTracking()
                .Where(l => locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var positionProfileTitles = positionProfileIds.Count > 0
            ? await dbContext.PositionProfiles
                .AsNoTracking()
                .Where(p => positionProfileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken)
            : new Dictionary<Guid, string>();

        var employmentTypeNames = employmentTypeIds.Count > 0
            ? await dbContext.EmploymentTypes
                .AsNoTracking()
                .Where(t => employmentTypeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        // Current compensation record per employee (FTE lives on Compensation, not Employee — a
        // time-effective record per employee). Sensitive/salary-adjacent data (05-database-standards) —
        // only the FTE value is projected here, never logged.
        //
        // CompensationRecordWriter's overlap check is meant to guarantee at most one "current"
        // (EffectiveFrom <= today && (EffectiveTo == null || EffectiveTo >= today)) record per
        // employee, but that check is read-then-write with no DB-level uniqueness constraint or
        // transaction isolation backing it, so a data anomaly (e.g. a race between two concurrent
        // writes for the same employee) is possible. Grouping and picking the most recently
        // effective record per employee keeps this report resilient to that instead of crashing
        // the whole page via ToDictionaryAsync's duplicate-key exception.
        var currentFteByEmployee = employeeIds.Count > 0
            ? (await dbContext.Compensations
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId &&
                            employeeIds.Contains(c.EmployeeId) &&
                            c.EffectiveFrom <= today &&
                            (c.EffectiveTo == null || c.EffectiveTo >= today))
                .Select(c => new { c.EmployeeId, c.EffectiveFrom, c.FTE })
                .ToListAsync(cancellationToken))
                .GroupBy(c => c.EmployeeId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(c => c.EffectiveFrom).First().FTE)
            : new Dictionary<Guid, decimal?>();

        var items = employees
            .Select(e => new HrHeadcountSummaryItem(
                e.Id,
                $"{e.FirstName} {e.LastName}",
                departmentNames.TryGetValue(e.DepartmentId, out var deptName) ? deptName : null,
                locationNames.TryGetValue(e.LocationId, out var locName) ? locName : null,
                positionProfileTitles.TryGetValue(e.PositionProfileId, out var posTitle) ? posTitle : null,
                employmentTypeNames.TryGetValue(e.EmploymentTypeId, out var etName) ? etName : null,
                e.Status.ToString(),
                e.StartDate,
                e.LeavingDate,
                currentFteByEmployee.TryGetValue(e.Id, out var fte) ? fte : null))
            .ToList();

        var totalHeadcount = employees.Count;
        var activeEmployees = employees.Count(e => e.Status == EmploymentStatus.Active);
        var futureStarters = employees.Count(e => e.StartDate > today);
        var leavers = employees.Count(e => e.LeavingDate != null);
        var totalFte = items.Sum(i => i.Fte ?? 0m);

        return new HrHeadcountSummaryResult(items, totalHeadcount, activeEmployees, futureStarters, leavers, totalFte);
    }
}
