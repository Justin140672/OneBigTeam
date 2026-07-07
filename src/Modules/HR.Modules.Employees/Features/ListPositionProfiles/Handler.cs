using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListPositionProfiles;

internal sealed class ListPositionProfilesHandler
{
    private readonly EmployeesDbContext _dbContext;

    public ListPositionProfilesHandler(EmployeesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ListPositionProfilesResponse>> HandleAsync(
        ListPositionProfilesRequest request,
        CancellationToken cancellationToken)
    {
        var profileQuery = _dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == request.CompanyId);

        if (!request.IncludeInactive)
            profileQuery = profileQuery.Where(p => p.IsActive);

        var items = await profileQuery
            .OrderBy(p => p.Title)
            .GroupJoin(
                _dbContext.Departments.AsNoTracking(),
                p => p.DepartmentId,
                d => d.Id,
                (p, depts) => new { Profile = p, Departments = depts })
            .SelectMany(
                x => x.Departments.DefaultIfEmpty(),
                (x, dept) => new PositionProfileListItem(
                    x.Profile.Id,
                    dept == null ? null : dept.Name,
                    x.Profile.Title,
                    x.Profile.Description,
                    x.Profile.IsActive,
                    x.Profile.SalaryMin,
                    x.Profile.SalaryMax,
                    x.Profile.SalaryType == null ? null : x.Profile.SalaryType.Value.ToString()))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListPositionProfilesResponse(items));
    }
}
