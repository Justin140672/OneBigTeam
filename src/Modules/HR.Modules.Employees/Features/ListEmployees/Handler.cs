using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListEmployees;

internal sealed class ListEmployeesHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IProfilePhotoReader _profilePhotoReader;
    private readonly IEmployeeUserAccountStatusReader _userAccountStatusReader;

    public ListEmployeesHandler(
        EmployeesDbContext dbContext,
        IProfilePhotoReader profilePhotoReader,
        IEmployeeUserAccountStatusReader userAccountStatusReader)
    {
        _dbContext = dbContext;
        _profilePhotoReader = profilePhotoReader;
        _userAccountStatusReader = userAccountStatusReader;
    }

    public async Task<Result<ListEmployeesResponse>> HandleAsync(
        ListEmployeesRequest request,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.FirstName.ToLower().Contains(search) ||
                e.LastName.ToLower().Contains(search) ||
                (e.FirstName.ToLower() + " " + e.LastName.ToLower()).Contains(search) ||
                e.WorkEmail.ToLower().Contains(search) ||
                e.EmployeeNumber.ToLower().Contains(search));
        }

        if (request.DepartmentId is not null)
            query = query.Where(e => e.DepartmentId == request.DepartmentId);

        if (request.PositionProfileId is not null)
            query = query.Where(e => e.PositionProfileId == request.PositionProfileId);

        if (request.Status is not null)
            query = query.Where(e => e.Status == request.Status);

        var totalCount = await query.CountAsync(cancellationToken);

        var employees = await query
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        // Resolve display names with two targeted lookups — no N+1
        var departmentIds = employees
            .Select(e => e.DepartmentId)
            .ToHashSet();

        var positionProfileIds = employees
            .Select(e => e.PositionProfileId)
            .ToHashSet();

        var managerIds = employees
            .Where(e => e.ManagerId is not null)
            .Select(e => e.ManagerId!.Value)
            .ToHashSet();

        var departmentNames = departmentIds.Count > 0
            ? await _dbContext.Departments
                .AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var locationIds = employees
            .Select(e => e.LocationId)
            .ToHashSet();

        var locationNames = locationIds.Count > 0
            ? await _dbContext.Locations
                .AsNoTracking()
                .Where(l => locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var positionProfileTitles = positionProfileIds.Count > 0
            ? await _dbContext.PositionProfiles
                .AsNoTracking()
                .Where(p => positionProfileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken)
            : new Dictionary<Guid, string>();

        var managerNames = managerIds.Count > 0
            ? await _dbContext.Employees
                .AsNoTracking()
                .Where(e => managerIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => $"{e.FirstName} {e.LastName}", cancellationToken)
            : new Dictionary<Guid, string>();

        // Employee.ProfileImageUrl is a legacy field that nothing writes to any more — current
        // profile photos live in the Documents module, resolved here via IProfilePhotoReader for
        // just the current page of results (same bulk, no-N+1 lookup style as the dictionaries above).
        var employeeIds = employees.Select(e => e.Id).ToList();
        var photoUrls = await _profilePhotoReader.GetCurrentPhotoUrlsAsync(
            request.CompanyId, employeeIds, cancellationToken);

        // Employees not present in the returned dictionary are treated as NoUser (see
        // IEmployeeUserAccountStatusReader contract) — no ApplicationUser and no active invite exist.
        var accountStatuses = await _userAccountStatusReader.GetStatusesAsync(
            request.CompanyId, employeeIds, cancellationToken);

        var items = employees
            .Select(e => new EmployeeListItem(
                e.Id,
                e.CompanyId,
                e.DepartmentId,
                departmentNames.TryGetValue(e.DepartmentId, out var deptName) ? deptName : null,
                e.LocationId,
                locationNames.TryGetValue(e.LocationId, out var locName) ? locName : null,
                e.PositionProfileId,
                positionProfileTitles.TryGetValue(e.PositionProfileId, out var ppTitle) ? ppTitle : null,
                e.ManagerId,
                e.ManagerId is not null && managerNames.TryGetValue(e.ManagerId.Value, out var mgrName) ? mgrName : null,
                e.FirstName,
                e.LastName,
                e.WorkEmail,
                e.StartDate,
                e.Status,
                e.CreatedAt,
                photoUrls.TryGetValue(e.Id, out var photoUrl) ? photoUrl : null,
                accountStatuses.TryGetValue(e.Id, out var accountSummary)
                    ? accountSummary.Status.ToString()
                    : EmployeeUserAccountStatus.NoUser.ToString()))
            .ToList();

        var totalPages = request.PageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / request.PageSize);

        return Result.Success(new ListEmployeesResponse(items, totalCount, request.PageNumber, request.PageSize, totalPages));
    }
}
