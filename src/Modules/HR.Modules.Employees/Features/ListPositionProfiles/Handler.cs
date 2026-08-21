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

        // Opt-in only — see Request.cs's remarks. Without a PageSize, this remains the original
        // unbounded "return every Position Profile the company has" query every other caller
        // already depends on.
        if (!string.IsNullOrWhiteSpace(request.Search))
            profileQuery = profileQuery.Where(p => EF.Functions.ILike(p.Title, $"%{request.Search}%"));

        var orderedQuery = profileQuery.OrderBy(p => p.Title).AsQueryable();

        if (request.PageSize is > 0)
            orderedQuery = orderedQuery.Take(request.PageSize.Value);

        var items = await orderedQuery
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
                    x.Profile.SalaryType == null ? null : x.Profile.SalaryType.Value.ToString(),
                    x.Profile.NoticePeriodUnitOverride,
                    x.Profile.NoticePeriodLengthOverride))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListPositionProfilesResponse(items));
    }
}
