using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetHeadcountSummary;

internal sealed class GetHeadcountSummaryHandler(EmployeesDbContext dbContext)
{
    private const string UnassignedLabel = "Unassigned";

    public async Task<GetHeadcountSummaryResponse> HandleAsync(
        GetHeadcountSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var counts = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId
                     && (e.Status == EmploymentStatus.Active || e.Status == EmploymentStatus.OnLeave))
            .GroupBy(e => e.DepartmentId)
            .Select(g => new { DepartmentId = g.Key, EmployeeCount = g.Count() })
            .ToListAsync(cancellationToken);

        var departmentIds = counts
            .Select(c => c.DepartmentId)
            .ToHashSet();

        var departmentNames = departmentIds.Count > 0
            ? await dbContext.Departments
                .AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var items = counts
            .Select(c => new HeadcountSummaryItem(
                c.DepartmentId,
                departmentNames.TryGetValue(c.DepartmentId, out var name)
                    ? name
                    : UnassignedLabel,
                c.EmployeeCount))
            .OrderBy(i => i.DepartmentName)
            .ToList();

        return new GetHeadcountSummaryResponse(items);
    }
}
