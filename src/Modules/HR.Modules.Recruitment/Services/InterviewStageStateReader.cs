using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

internal static class InterviewStageStateReader
{
    public static async Task<IReadOnlyDictionary<Guid, InterviewStageState>> LoadAsync(
        RecruitmentDbContext db,
        Guid companyId,
        IReadOnlyCollection<(Guid ApplicationId, Guid CurrentStageId)> applications,
        CancellationToken cancellationToken)
    {
        if (applications.Count == 0)
            return new Dictionary<Guid, InterviewStageState>();

        var stages = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        var stagesById = stages.ToDictionary(s => s.Id);
        var applicationIds = applications.Select(a => a.ApplicationId).ToList();

        var interviewsByApplication = (await db.Interviews
                .AsNoTracking()
                .Where(i => i.CompanyId == companyId && applicationIds.Contains(i.ApplicationId))
                .ToListAsync(cancellationToken))
            .ToLookup(i => i.ApplicationId);

        var result = new Dictionary<Guid, InterviewStageState>();
        foreach (var (applicationId, currentStageId) in applications)
        {
            if (!stagesById.TryGetValue(currentStageId, out var currentStage))
                continue;

            result[applicationId] = InterviewStageWorkflow.Evaluate(
                currentStage, stages, interviewsByApplication[applicationId]);
        }

        return result;
    }
}
