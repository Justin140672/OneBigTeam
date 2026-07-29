using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeStarterReader(
    EmployeesDbContext dbContext,
    IEmployeeRecruiterReader recruiterReader,
    IOnboardingStatusReader onboardingStatusReader,
    IProbationSummaryReader probationSummaryReader) : IEmployeeStarterReader
{
    public async Task<PagedResult<EmployeeStarterReportItem>> GetEmployeeStartersAsync(
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

        if (filter.PositionProfileId is not null)
            query = query.Where(e => e.PositionProfileId == filter.PositionProfileId);

        if (filter.LocationId is not null)
            query = query.Where(e => e.LocationId == filter.LocationId);

        if (filter.EmploymentTypeId is not null)
            query = query.Where(e => e.EmploymentTypeId == filter.EmploymentTypeId);

        if (filter.DateRangeStart is not null)
            query = query.Where(e => e.StartDate >= filter.DateRangeStart);

        if (filter.DateRangeEnd is not null)
            query = query.Where(e => e.StartDate <= filter.DateRangeEnd);

        query = sortBy?.ToLowerInvariant() switch
        {
            "name" => sortDescending
                ? query.OrderByDescending(e => e.LastName).ThenByDescending(e => e.FirstName)
                : query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName),
            "startdate" or _ => sortDescending
                ? query.OrderByDescending(e => e.StartDate)
                : query.OrderBy(e => e.StartDate),
        };

        var totalCount = await query.CountAsync(cancellationToken);

        var employees = await query
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        var employeeIds = employees.Select(e => e.Id).ToList();
        var departmentIds = employees.Select(e => e.DepartmentId).ToHashSet();
        var positionProfileIds = employees.Select(e => e.PositionProfileId).ToHashSet();

        var departmentNames = departmentIds.Count > 0
            ? await dbContext.Departments.AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var positionProfileTitles = positionProfileIds.Count > 0
            ? await dbContext.PositionProfiles.AsNoTracking()
                .Where(p => positionProfileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken)
            : new Dictionary<Guid, string>();

        // Recruiter is a batch lookup; onboarding/probation status are single-employee reader
        // contracts (IOnboardingStatusReader/IProbationSummaryReader) called per row of the
        // CURRENT PAGE only (bounded to pagination.PageSize), never against the full company —
        // avoids needing a new batch-shaped contract for those two while still staying bounded.
        var recruiterNames = await recruiterReader.GetRecruiterNamesAsync(companyId, employeeIds, cancellationToken);

        var items = new List<EmployeeStarterReportItem>(employees.Count);
        foreach (var e in employees)
        {
            var onboardingStatus = await onboardingStatusReader.GetStatusAsync(companyId, e.Id, cancellationToken);
            var probationStatus = await probationSummaryReader.GetSummaryAsync(companyId, e.Id, cancellationToken);

            items.Add(new EmployeeStarterReportItem(
                e.Id,
                $"{e.FirstName} {e.LastName}",
                e.StartDate,
                recruiterNames.TryGetValue(e.Id, out var recruiter) ? recruiter : null,
                departmentNames.TryGetValue(e.DepartmentId, out var deptName) ? deptName : null,
                positionProfileTitles.TryGetValue(e.PositionProfileId, out var posTitle) ? posTitle : null,
                onboardingStatus?.Status,
                probationStatus?.Status));
        }

        return new PagedResult<EmployeeStarterReportItem>(items, totalCount, pagination.PageNumber, pagination.PageSize);
    }
}
