using HR.Modules.Employees.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class DirectReportsReader(EmployeesDbContext dbContext) : IDirectReportsReader
{
    public async Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.ManagerId == managerId)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetAllDescendantIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken)
    {
        // Bulk-load a single (Id, ManagerId) projection for the whole company, then walk it
        // in memory with a BFS closure. This mirrors GetOrganisationChartHandler's approach of
        // pulling the flat employee set once and building relationships client-side, rather than
        // issuing provider-specific raw SQL (e.g. a recursive CTE) — nothing else in this
        // codebase uses FromSqlRaw/FromSqlInterpolated, and company employee counts in this
        // domain make an in-memory walk cheap.
        var managerLookup = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .Select(e => new { e.Id, e.ManagerId })
            .ToListAsync(cancellationToken);

        var reportsByManager = managerLookup
            .Where(e => e.ManagerId.HasValue)
            .GroupBy(e => e.ManagerId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToList());

        var descendants = new List<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(managerId);
        var visited = new HashSet<Guid> { managerId };

        while (queue.Count > 0)
        {
            var currentManagerId = queue.Dequeue();
            if (!reportsByManager.TryGetValue(currentManagerId, out var directReports))
                continue;

            foreach (var reportId in directReports)
            {
                if (!visited.Add(reportId))
                    continue;

                descendants.Add(reportId);
                queue.Enqueue(reportId);
            }
        }

        return descendants;
    }
}
