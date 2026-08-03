using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetGenderSplit;

internal sealed class GetGenderSplitHandler(EmployeesDbContext dbContext)
{
    private const string NotSpecifiedLabel = "Not Specified";

    public async Task<GetGenderSplitResponse> HandleAsync(
        GetGenderSplitRequest request,
        CancellationToken cancellationToken)
    {
        var counts = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId
                     && e.Status == EmploymentStatus.Active)
            .GroupBy(e => e.Gender)
            .Select(g => new { Gender = g.Key, EmployeeCount = g.Count() })
            .ToListAsync(cancellationToken);

        var totalCount = counts.Sum(c => c.EmployeeCount);

        if (totalCount == 0)
        {
            return new GetGenderSplitResponse(new List<GenderSplitItem>());
        }

        var grouped = counts
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Gender) ? NotSpecifiedLabel : c.Gender)
            .Select(g => new
            {
                Gender = g.Key,
                EmployeeCount = g.Sum(c => c.EmployeeCount),
            })
            .OrderBy(g => g.Gender)
            .ToList();

        var items = BuildItemsWithPercentages(grouped.Select(g => (g.Gender, g.EmployeeCount)).ToList(), totalCount);

        return new GetGenderSplitResponse(items);
    }

    private static List<GenderSplitItem> BuildItemsWithPercentages(
        List<(string Gender, int EmployeeCount)> groups,
        int totalCount)
    {
        var rawPercentages = groups
            .Select(g => Math.Round(g.EmployeeCount * 100d / totalCount, 1))
            .ToList();

        var residual = Math.Round(100d - rawPercentages.Sum(), 1);

        if (residual != 0 && groups.Count > 0)
        {
            var largestIndex = groups
                .Select((g, i) => (g.EmployeeCount, Index: i))
                .OrderByDescending(x => x.EmployeeCount)
                .First().Index;

            rawPercentages[largestIndex] = Math.Round(rawPercentages[largestIndex] + residual, 1);
        }

        return groups
            .Select((g, i) => new GenderSplitItem(g.Gender, g.EmployeeCount, rawPercentages[i]))
            .ToList();
    }
}
