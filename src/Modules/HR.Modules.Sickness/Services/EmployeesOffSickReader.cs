using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Services;

internal sealed class EmployeesOffSickReader(SicknessDbContext dbContext) : IEmployeesOffSickReader
{
    public async Task<IReadOnlySet<Guid>> GetOffSickEmployeeIdsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        DateOnly onDate,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<Guid>();

        var offSick = await dbContext.SicknessRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId
                     && ids.Contains(r.EmployeeId)
                     && r.Status == SicknessStatus.Active
                     && r.StartDate <= onDate)
            .Select(r => r.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return offSick.ToHashSet();
    }
}
