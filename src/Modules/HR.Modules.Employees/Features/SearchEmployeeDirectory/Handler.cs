using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.SearchEmployeeDirectory;

internal sealed class SearchEmployeeDirectoryHandler
{
    private readonly EmployeesDbContext _dbContext;

    public SearchEmployeeDirectoryHandler(EmployeesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SearchEmployeeDirectoryResponse>> HandleAsync(
        SearchEmployeeDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId);

        if (!string.IsNullOrWhiteSpace(request.Term))
        {
            var term = request.Term.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.FirstName.ToLower().Contains(term) ||
                e.LastName.ToLower().Contains(term) ||
                (e.FirstName.ToLower() + " " + e.LastName.ToLower()).Contains(term) ||
                e.WorkEmail.ToLower().Contains(term) ||
                e.EmployeeNumber.ToLower().Contains(term));
        }

        if (!request.IncludeLeavers)
        {
            query = query.Where(e =>
                e.Status != EmploymentStatus.Leaving &&
                e.Status != EmploymentStatus.FormerEmployee);
        }

        var employees = await query
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Take(request.Limit)
            .ToListAsync(cancellationToken);

        var departmentIds = employees.Select(e => e.DepartmentId).ToHashSet();
        var positionProfileIds = employees.Select(e => e.PositionProfileId).ToHashSet();

        var departmentNames = departmentIds.Count > 0
            ? await _dbContext.Departments
                .AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var positionProfileTitles = positionProfileIds.Count > 0
            ? await _dbContext.PositionProfiles
                .AsNoTracking()
                .Where(p => positionProfileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken)
            : new Dictionary<Guid, string>();

        var items = employees
            .Select(e => new SearchEmployeeDirectoryItem(
                e.Id,
                e.FirstName,
                e.LastName,
                e.EmployeeNumber,
                positionProfileTitles.TryGetValue(e.PositionProfileId, out var ppTitle) ? ppTitle : null,
                departmentNames.TryGetValue(e.DepartmentId, out var deptName) ? deptName : null,
                e.Status))
            .ToList();

        return Result.Success(new SearchEmployeeDirectoryResponse(items));
    }
}
