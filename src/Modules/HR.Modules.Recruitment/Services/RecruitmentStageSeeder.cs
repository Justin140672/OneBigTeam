using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

internal sealed class RecruitmentStageSeeder(RecruitmentDbContext db)
{
    public async Task EnsureDefaultStagesSeededAsync(Guid companyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var alreadySeeded = await db.RecruitmentStages
            .AsNoTracking()
            .AnyAsync(s => s.CompanyId == companyId, cancellationToken);

        if (alreadySeeded)
            return;

        db.RecruitmentStages.AddRange(BuildDefaultStages(companyId, now));
        await db.SaveChangesAsync(cancellationToken);
    }

    public static IReadOnlyList<RecruitmentStage> BuildDefaultStages(Guid companyId, DateTimeOffset now) =>
    [
        RecruitmentStage.Create(Guid.NewGuid(), companyId, "Application Received", 1, false, RecruitmentStageTerminalOutcome.None, now, RecruitmentStagePurpose.NewApplication),
        RecruitmentStage.Create(Guid.NewGuid(), companyId, "CV Review",            2, false, RecruitmentStageTerminalOutcome.None, now),
        RecruitmentStage.Create(Guid.NewGuid(), companyId, "Interview",            3, false, RecruitmentStageTerminalOutcome.None, now, RecruitmentStagePurpose.Interview),
        RecruitmentStage.Create(Guid.NewGuid(), companyId, "Offer",                4, false, RecruitmentStageTerminalOutcome.None, now, RecruitmentStagePurpose.Offer),
        RecruitmentStage.Create(Guid.NewGuid(), companyId, "Hired",                5, true,  RecruitmentStageTerminalOutcome.Hired, now),
        RecruitmentStage.Create(Guid.NewGuid(), companyId, "Rejected",             6, true,  RecruitmentStageTerminalOutcome.Rejected, now),
    ];
}
