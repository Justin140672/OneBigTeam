using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Services;

internal sealed class EmployeeSicknessStatusReader(SicknessDbContext dbContext) : IEmployeeSicknessStatusReader
{
    public async Task<IReadOnlySet<Guid>> GetSickEmployeeIdsAsync(
        Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<Guid>();

        var sickIds = await dbContext.SicknessRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId
                     && r.Status == SicknessStatus.Active
                     && ids.Contains(r.EmployeeId))
            .Select(r => r.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return sickIds.ToHashSet();
    }
}
