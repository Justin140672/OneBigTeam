using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeInviteCandidateReader(EmployeesDbContext dbContext) : IEmployeeInviteCandidateReader
{
    public async Task<IReadOnlyList<EmployeeInviteCandidate>> GetCandidatesAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        // Current employees only — a former employee should never be re-invited as a user.
        var employees = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Status != EmploymentStatus.FormerEmployee)
            .Select(e => new
            {
                e.Id,
                e.FirstName,
                e.LastName,
                e.WorkEmail,
                e.PositionProfileId,
            })
            .ToListAsync(cancellationToken);

        if (employees.Count == 0)
            return [];

        var positionProfileIds = employees.Select(e => e.PositionProfileId).Distinct().ToList();

        var positionTitles = await dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && positionProfileIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

        return employees
            .Select(e => new EmployeeInviteCandidate(
                e.Id,
                $"{e.FirstName} {e.LastName}".Trim(),
                string.IsNullOrWhiteSpace(e.WorkEmail) ? null : e.WorkEmail,
                e.PositionProfileId,
                positionTitles.TryGetValue(e.PositionProfileId, out var title) ? title : null))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
