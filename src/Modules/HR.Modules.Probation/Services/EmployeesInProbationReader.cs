using HR.Infrastructure.Abstractions;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Services;

/// <summary>
/// DSH-05 implementation of <see cref="IEmployeesInProbationReader"/>. "In probation" = an active
/// probation record (Active / ReviewDue / Extended). NotStarted, Passed, Failed and NotApplicable
/// are excluded — see the interface doc for why a review being due is not the same as being in
/// probation.
/// </summary>
internal sealed class EmployeesInProbationReader(ProbationDbContext dbContext) : IEmployeesInProbationReader
{
    private static readonly ProbationStatus[] ActiveStatuses =
        [ProbationStatus.Active, ProbationStatus.ReviewDue, ProbationStatus.Extended];

    public async Task<IReadOnlySet<Guid>> GetEmployeeIdsInProbationAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<Guid>();

        var inProbation = await dbContext.ProbationRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId
                     && ids.Contains(r.EmployeeId)
                     && ActiveStatuses.Contains(r.Status))
            .Select(r => r.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return inProbation.ToHashSet();
    }
}
