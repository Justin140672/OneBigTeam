using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetRecruitmentStageUsage;

internal sealed class GetRecruitmentStageUsageHandler(RecruitmentDbContext db)
{
    // Cap the labels returned to keep the confirmation dialog readable — the count above already
    // conveys the true scale for larger numbers.
    private const int MaxVacancyLabels = 5;

    public async Task<Result<GetRecruitmentStageUsageResponse>> HandleAsync(
        GetRecruitmentStageUsageRequest request,
        CancellationToken cancellationToken)
    {
        var stageExists = await db.RecruitmentStages
            .AnyAsync(s => s.Id == request.RecruitmentStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (!stageExists)
            return Result.Failure<GetRecruitmentStageUsageResponse>(
                Error.NotFound($"Recruitment stage '{request.RecruitmentStageId}' was not found."));

        var vacancies = await (
            from a in db.Applications.AsNoTracking()
            join v in db.Vacancies.AsNoTracking() on a.VacancyId equals v.Id
            where a.CompanyId == request.CompanyId
               && a.CurrentStageId == request.RecruitmentStageId
               && v.CompanyId == request.CompanyId
               && v.Status != VacancyStatus.Closed
               && v.Status != VacancyStatus.Cancelled
            select new { v.Id, v.AdvertTitle })
            .Distinct()
            .ToListAsync(cancellationToken);

        var labels = vacancies
            .Take(MaxVacancyLabels)
            .Select(v => string.IsNullOrWhiteSpace(v.AdvertTitle) ? $"Vacancy {v.Id.ToString()[..8]}" : v.AdvertTitle!)
            .ToList();

        return Result.Success(new GetRecruitmentStageUsageResponse(
            request.RecruitmentStageId,
            vacancies.Count > 0,
            vacancies.Count,
            labels));
    }
}
