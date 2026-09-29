using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

internal sealed class EmployeeRecruiterReader(RecruitmentDbContext dbContext, IEmployeeNameReader employeeNameReader) : IEmployeeRecruiterReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetRecruiterNamesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds.ToHashSet();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var hires = await dbContext.Candidates
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.EmployeeId != null && ids.Contains(c.EmployeeId!.Value))
            .Select(c => new
            {
                EmployeeId = c.EmployeeId!.Value,
                CandidateId = c.Id,
            })
            .ToListAsync(cancellationToken);

        if (hires.Count == 0)
            return new Dictionary<Guid, string>();

        var candidateIds = hires.Select(h => h.CandidateId).ToHashSet();

        // Internal recruitment Ticket 4: a hired candidate's linked record is reused when that employee
        // later applies internally, so Internal-source applications are excluded — "recruited by" is
        // about how the employee originally joined — and the earliest remaining application is taken
        // deterministically.
        var applications = await dbContext.Applications
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId
                && candidateIds.Contains(a.CandidateId)
                && (a.Source == null || a.Source != ApplicationSource.Internal))
            .Select(a => new { a.CandidateId, a.VacancyId, a.AppliedAt })
            .ToListAsync(cancellationToken);

        var vacancyByCandidateId = applications
            .GroupBy(a => a.CandidateId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.AppliedAt).First().VacancyId);

        var vacancyIds = vacancyByCandidateId.Values.ToHashSet();

        var vacancies = await dbContext.Vacancies
            .AsNoTracking()
            .Where(v => vacancyIds.Contains(v.Id))
            .Select(v => new { v.Id, v.AssignedRecruiterId, v.HiringManagerId })
            .ToListAsync(cancellationToken);

        var vacancyById = vacancies.ToDictionary(v => v.Id);

        var externalRecruiterIds = vacancies
            .Where(v => v.AssignedRecruiterId is not null)
            .Select(v => v.AssignedRecruiterId!.Value)
            .ToHashSet();

        var externalRecruiterNames = externalRecruiterIds.Count > 0
            ? await dbContext.ExternalRecruiters
                .AsNoTracking()
                .Where(r => externalRecruiterIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.AgencyName, cancellationToken)
            : new Dictionary<Guid, string>();

        var hiringManagerIds = vacancies
            .Where(v => v.AssignedRecruiterId is null)
            .Select(v => v.HiringManagerId)
            .ToHashSet();

        var hiringManagerNames = hiringManagerIds.Count > 0
            ? await employeeNameReader.GetNamesAsync(companyId, hiringManagerIds, cancellationToken)
            : new Dictionary<Guid, string>();

        var result = new Dictionary<Guid, string>();

        foreach (var hire in hires)
        {
            if (!vacancyByCandidateId.TryGetValue(hire.CandidateId, out var vacancyId))
                continue;
            if (!vacancyById.TryGetValue(vacancyId, out var vacancy))
                continue;

            if (vacancy.AssignedRecruiterId is not null &&
                externalRecruiterNames.TryGetValue(vacancy.AssignedRecruiterId.Value, out var agencyName))
            {
                result[hire.EmployeeId] = agencyName;
            }
            else if (hiringManagerNames.TryGetValue(vacancy.HiringManagerId, out var managerName))
            {
                result[hire.EmployeeId] = $"{managerName} (Hiring Manager)";
            }
        }

        return result;
    }
}
