using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

/// <summary>
/// Ticket #98: seeds the six default RecruitmentStage rows the first time a company actually starts
/// using Recruitment. Judgement call: the platform has no generic "module enabled for a company"
/// hook and no CompanyCreated integration event to subscribe to (verified — HR.Modules.Companies
/// publishes no such event, and no other module hooks into anything resembling module enablement).
/// The most sensible existing integration point is therefore the moment recruitment data is first
/// created for a company — creating the first Vacancy (see CreateVacancyHandler) — mirroring the
/// per-company idempotent seeding pattern RecruitmentModule.SeedRecruitmentAsync already uses for
/// demo data. This is also invoked from the ticket #99 data migration (for companies with existing
/// applications but no stages yet) and is safe to call unconditionally any number of times: it is a
/// no-op once a company already has at least one RecruitmentStage row, so enabling/using recruitment
/// twice never duplicates stages. Stages remain fully editable afterward via the #97 CRUD endpoints.
/// </summary>
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
        RecruitmentStage.Create(Guid.NewGuid(), companyId, "Application Received", 1, false, RecruitmentStageTerminalOutcome.None, now),
        RecruitmentStage.Create(Guid.NewGuid(), companyId, "CV Review",            2, false, RecruitmentStageTerminalOutcome.None, now),
        RecruitmentStage.Create(Guid.NewGuid(), companyId, "Interview",            3, false, RecruitmentStageTerminalOutcome.None, now),
        RecruitmentStage.Create(Guid.NewGuid(), companyId, "Offer",                4, false, RecruitmentStageTerminalOutcome.None, now),
        RecruitmentStage.Create(Guid.NewGuid(), companyId, "Hired",                5, true,  RecruitmentStageTerminalOutcome.Hired, now),
        RecruitmentStage.Create(Guid.NewGuid(), companyId, "Rejected",             6, true,  RecruitmentStageTerminalOutcome.Rejected, now),
    ];
}
