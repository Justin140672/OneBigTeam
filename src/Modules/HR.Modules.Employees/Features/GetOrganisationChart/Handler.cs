using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetOrganisationChart;

internal sealed class GetOrganisationChartHandler
{
    private readonly EmployeesDbContext _dbContext;

    public GetOrganisationChartHandler(EmployeesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetOrganisationChartResponse>> HandleAsync(
        GetOrganisationChartRequest request,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId);

        // Status is an optional filter rather than a hardcoded "Active only" — the caller (the
        // Organisation Chart page) defaults its own Status dropdown to Active, but HR can widen
        // or change this, e.g. to review who's on leave or check a leaver's old reporting line.
        if (request.Status is not null)
            query = query.Where(e => e.Status == request.Status);

        if (request.DepartmentId is not null)
            query = query.Where(e => e.DepartmentId == request.DepartmentId);

        if (request.LocationId is not null)
            query = query.Where(e => e.LocationId == request.LocationId);

        var employees = await query
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(cancellationToken);

        // Resolve display names with three targeted lookups — no N+1.
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

        var items = employees
            .Select(e => new OrganisationChartEmployeeItem(
                e.Id,
                $"{e.FirstName} {e.LastName}",
                e.EmployeeNumber,
                positionProfileTitles.TryGetValue(e.PositionProfileId, out var title) ? title : string.Empty,
                departmentNames.TryGetValue(e.DepartmentId, out var deptName) ? deptName : string.Empty,
                e.ManagerId,
                locationNames.TryGetValue(e.LocationId, out var locName) ? locName : string.Empty,
                e.ProfileImageUrl))
            .ToList();

        return Result.Success(new GetOrganisationChartResponse(items));
    }
}
