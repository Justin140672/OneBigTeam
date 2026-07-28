using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetExternalRecruiterUsage;

internal sealed class GetExternalRecruiterUsageHandler(RecruitmentDbContext db)
{
    private const int MaxVacancyLabels = 5;

    public async Task<Result<GetExternalRecruiterUsageResponse>> HandleAsync(
        GetExternalRecruiterUsageRequest request,
        CancellationToken cancellationToken)
    {
        var recruiterExists = await db.ExternalRecruiters
            .AnyAsync(r => r.Id == request.ExternalRecruiterId && r.CompanyId == request.CompanyId, cancellationToken);

        if (!recruiterExists)
            return Result.Failure<GetExternalRecruiterUsageResponse>(
                Error.NotFound($"External recruiter '{request.ExternalRecruiterId}' was not found."));

        var vacancies = await db.Vacancies
            .AsNoTracking()
            .Where(v => v.CompanyId == request.CompanyId
                     && v.AssignedRecruiterId == request.ExternalRecruiterId
                     && v.Status != VacancyStatus.Closed
                     && v.Status != VacancyStatus.Cancelled)
            .Select(v => new { v.Id, v.AdvertTitle })
            .ToListAsync(cancellationToken);

        var labels = vacancies
            .Take(MaxVacancyLabels)
            .Select(v => string.IsNullOrWhiteSpace(v.AdvertTitle) ? $"Vacancy {v.Id.ToString()[..8]}" : v.AdvertTitle!)
            .ToList();

        return Result.Success(new GetExternalRecruiterUsageResponse(
            request.ExternalRecruiterId,
            vacancies.Count > 0,
            vacancies.Count,
            labels));
    }
}
