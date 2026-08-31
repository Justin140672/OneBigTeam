using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

/// <summary>
/// Shared helper for seeding RecruitmentStage rows in tests (ticket #99). Most handler tests need at
/// least a non-terminal "current" stage plus active Hired/Rejected terminal stages to satisfy the
/// FK on Application.CurrentStageId and the handlers' own stage lookups — this centralises that setup
/// instead of duplicating six RecruitmentStage.Create calls in every test file.
/// </summary>
internal static class RecruitmentStageTestData
{
    /// <summary>
    /// Seeds the standard six-stage pipeline (mirroring RecruitmentStageSeeder.BuildDefaultStages) for
    /// the given company directly into the DbContext (not saved — callers should SaveChangesAsync
    /// alongside their other seed data).
    /// </summary>
    public static SeededStages AddDefaultStages(RecruitmentDbContext db, Guid companyId, DateTimeOffset now, bool withPurposes = true)
    {
        var applicationReceived = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Application Received", 1, false, RecruitmentStageTerminalOutcome.None, now, withPurposes ? RecruitmentStagePurpose.NewApplication : null);
        var cvReview            = RecruitmentStage.Create(Guid.NewGuid(), companyId, "CV Review",            2, false, RecruitmentStageTerminalOutcome.None, now);
        var interview            = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Interview",            3, false, RecruitmentStageTerminalOutcome.None, now, withPurposes ? RecruitmentStagePurpose.Interview : null);
        var offer                = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Offer",                4, false, RecruitmentStageTerminalOutcome.None, now, withPurposes ? RecruitmentStagePurpose.Offer : null);
        var hired                = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Hired",                5, true,  RecruitmentStageTerminalOutcome.Hired, now);
        var rejected             = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Rejected",             6, true,  RecruitmentStageTerminalOutcome.Rejected, now);

        db.RecruitmentStages.AddRange(applicationReceived, cvReview, interview, offer, hired, rejected);

        return new SeededStages(applicationReceived, cvReview, interview, offer, hired, rejected);
    }

    internal sealed record SeededStages(
        RecruitmentStage ApplicationReceived,
        RecruitmentStage CvReview,
        RecruitmentStage Interview,
        RecruitmentStage Offer,
        RecruitmentStage Hired,
        RecruitmentStage Rejected);
}
