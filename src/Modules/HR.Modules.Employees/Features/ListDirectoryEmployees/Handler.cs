using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListDirectoryEmployees;

internal sealed class ListDirectoryEmployeesHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IProfilePhotoReader _profilePhotoReader;

    public ListDirectoryEmployeesHandler(
        EmployeesDbContext dbContext,
        IProfilePhotoReader profilePhotoReader)
    {
        _dbContext = dbContext;
        _profilePhotoReader = profilePhotoReader;
    }

    public async Task<Result<ListDirectoryEmployeesResponse>> HandleAsync(
        ListDirectoryEmployeesRequest request,
        CancellationToken cancellationToken)
    {
        // Directory is employee-facing and only ever shows current colleagues.
        var query = _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId && e.Status == EmploymentStatus.Active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();

            var matchingDeptIds = _dbContext.Departments
                .Where(d => d.CompanyId == request.CompanyId && d.Name.ToLower().Contains(search))
                .Select(d => d.Id);
            var matchingPosIds = _dbContext.PositionProfiles
                .Where(p => p.CompanyId == request.CompanyId && p.Title.ToLower().Contains(search))
                .Select(p => p.Id);
            query = query.Where(e =>
                e.FirstName.ToLower().Contains(search) ||
                e.LastName.ToLower().Contains(search) ||
                (e.FirstName.ToLower() + " " + e.LastName.ToLower()).Contains(search) ||
                (e.PreferredName != null && e.PreferredName.ToLower().Contains(search)) ||
                e.WorkEmail.ToLower().Contains(search) ||
                matchingDeptIds.Contains(e.DepartmentId) ||
                matchingPosIds.Contains(e.PositionProfileId));
        }

        if (request.DepartmentId is not null)
            query = query.Where(e => e.DepartmentId == request.DepartmentId);

        if (request.LocationId is not null)
            query = query.Where(e => e.LocationId == request.LocationId);

        var totalCount = await query.CountAsync(cancellationToken);

        var employees = await query
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        // Resolve display names with targeted bulk lookups — no N+1 (mirrors ListEmployeesHandler).
        var departmentIds = employees.Select(e => e.DepartmentId).ToHashSet();
        var locationIds = employees.Select(e => e.LocationId).ToHashSet();
        var positionProfileIds = employees.Select(e => e.PositionProfileId).ToHashSet();

        var departmentNames = departmentIds.Count > 0
            ? await _dbContext.Departments
                .AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken)
            : new Dictionary<Guid, string>();

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

        var employeeIds = employees.Select(e => e.Id).ToList();
        var photoUrls = await _profilePhotoReader.GetCurrentPhotoUrlsAsync(
            request.CompanyId, employeeIds, cancellationToken);

        var items = employees
            .Select(e => new DirectoryEmployeeListItem(
                e.Id,
                e.FirstName,
                e.LastName,
                e.PreferredName,
                positionProfileTitles.TryGetValue(e.PositionProfileId, out var ppTitle) ? ppTitle : null,
                e.DepartmentId,
                departmentNames.TryGetValue(e.DepartmentId, out var deptName) ? deptName : null,
                e.LocationId,
                locationNames.TryGetValue(e.LocationId, out var locName) ? locName : null,
                e.WorkEmail,
                photoUrls.TryGetValue(e.Id, out var photoUrl) ? photoUrl : null))
            .ToList();

        var totalPages = request.PageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / request.PageSize);

        return Result.Success(new ListDirectoryEmployeesResponse(
            items, totalCount, request.PageNumber, request.PageSize, totalPages));
    }
}
