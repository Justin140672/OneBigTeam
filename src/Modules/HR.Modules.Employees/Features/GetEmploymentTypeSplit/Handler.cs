using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetEmploymentTypeSplit;

internal sealed class GetEmploymentTypeSplitHandler(EmployeesDbContext dbContext)
{
    public async Task<GetEmploymentTypeSplitResponse> HandleAsync(
        GetEmploymentTypeSplitRequest request,
        CancellationToken cancellationToken)
    {
        var counts = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId)
            .Where(HeadcountRules.ActiveExpression(DateOnly.FromDateTime(DateTime.UtcNow.Date)))
            .GroupBy(e => e.EmploymentTypeId)
            .Select(g => new { EmploymentTypeId = g.Key, EmployeeCount = g.Count() })
            .ToListAsync(cancellationToken);

        var totalCount = counts.Sum(c => c.EmployeeCount);

        if (totalCount == 0)
        {
            return new GetEmploymentTypeSplitResponse(new List<EmploymentTypeSplitItem>());
        }

        var employmentTypeIds = counts
            .Select(c => c.EmploymentTypeId)
            .ToHashSet();

        var employmentTypeNames = await EmploymentTypeGrouping.LoadLabelsAsync(
            dbContext, request.CompanyId, employmentTypeIds, cancellationToken);

        var groups = counts
            .Select(c => (
                EmploymentTypeId: (Guid?)c.EmploymentTypeId,
                EmploymentTypeName: EmploymentTypeGrouping.Resolve(employmentTypeNames, c.EmploymentTypeId).Label,
                c.EmployeeCount))
            .OrderBy(g => g.EmploymentTypeName)
            .ToList();

        var items = BuildItemsWithPercentages(groups, totalCount);

        return new GetEmploymentTypeSplitResponse(items);
    }

    private static List<EmploymentTypeSplitItem> BuildItemsWithPercentages(
        List<(Guid? EmploymentTypeId, string EmploymentTypeName, int EmployeeCount)> groups,
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
            .Select((g, i) => new EmploymentTypeSplitItem(
                g.EmploymentTypeId,
                g.EmploymentTypeName,
                g.EmployeeCount,
                rawPercentages[i]))
            .ToList();
    }
}
