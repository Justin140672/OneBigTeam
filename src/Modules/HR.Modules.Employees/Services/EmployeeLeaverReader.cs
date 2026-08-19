using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeLeaverReader(
    EmployeesDbContext dbContext,
    IOffboardingDetailReader offboardingDetailReader,
    IEmployeeUserAccountStatusReader accountStatusReader) : IEmployeeLeaverReader
{
    public async Task<PagedResult<EmployeeLeaverReportItem>> GetEmployeeLeaversAsync(
        Guid companyId,
        ReportFilterCriteria filter,
        Pagination pagination,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.LeavingDate != null);

        if (filter.DepartmentId is not null)
            query = query.Where(e => e.DepartmentId == filter.DepartmentId);

        if (filter.PositionProfileId is not null)
            query = query.Where(e => e.PositionProfileId == filter.PositionProfileId);

        if (filter.DateRangeStart is not null)
            query = query.Where(e => e.LeavingDate >= filter.DateRangeStart);

        if (filter.DateRangeEnd is not null)
            query = query.Where(e => e.LeavingDate <= filter.DateRangeEnd);

        query = sortBy?.ToLowerInvariant() switch
        {
            "name" => sortDescending
                ? query.OrderByDescending(e => e.LastName).ThenByDescending(e => e.FirstName)
                : query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName),
            "leavingdate" or _ => sortDescending
                ? query.OrderByDescending(e => e.LeavingDate)
                : query.OrderBy(e => e.LeavingDate),
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

        var accountStatuses = await accountStatusReader.GetStatusesAsync(companyId, employeeIds, cancellationToken);

        var items = new List<EmployeeLeaverReportItem>(employees.Count);
        foreach (var e in employees)
        {
            var offboardingDetail = await offboardingDetailReader.GetDetailAsync(companyId, e.Id, cancellationToken);
            var accountStatus = accountStatuses.TryGetValue(e.Id, out var summary)
                ? summary.Status.ToString()
                : "NoUser";

            items.Add(new EmployeeLeaverReportItem(
                e.Id,
                $"{e.FirstName} {e.LastName}",
                e.LeavingDate,
                offboardingDetail?.LastWorkingDay,
                departmentNames.TryGetValue(e.DepartmentId, out var deptName) ? deptName : null,
                positionProfileTitles.TryGetValue(e.PositionProfileId, out var posTitle) ? posTitle : null,
                Reason: null,
                offboardingDetail?.Status,
                accountStatus));
        }

        return new PagedResult<EmployeeLeaverReportItem>(items, totalCount, pagination.PageNumber, pagination.PageSize);
    }
}
