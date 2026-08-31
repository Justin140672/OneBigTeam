using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetInterviewsRequiringActionMetric;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;

namespace HR.Modules.Recruitment.Tests;

public class GetInterviewsRequiringActionMetricHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Counts_Pending_Interviews_In_The_Past_And_Today_Not_Future()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 3);
        var apps = AddApplications(db, companyId, vacancy.Id, candidates);

        db.Interviews.AddRange(
            Interview.Create(Guid.NewGuid(), companyId, apps[0].Id, Guid.NewGuid(), Now.AddDays(-1), 30, "Room 1", Now),
            Interview.Create(Guid.NewGuid(), companyId, apps[1].Id, Guid.NewGuid(), new DateTimeOffset(2026, 7, 6, 23, 0, 0, TimeSpan.Zero), 30, "Room 2", Now),
            Interview.Create(Guid.NewGuid(), companyId, apps[2].Id, Guid.NewGuid(), Now.AddDays(2), 30, "Room 3", Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.Equal(2, result.Count);
        Assert.Equal(result.Count, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Cancelled_And_Completed_Outcomes()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 5);
        var apps = AddApplications(db, companyId, vacancy.Id, candidates);

        var pending = Interview.Create(Guid.NewGuid(), companyId, apps[0].Id, Guid.NewGuid(), Now.AddDays(-1), 30, null, Now);
        var cancelled = Interview.Create(Guid.NewGuid(), companyId, apps[1].Id, Guid.NewGuid(), Now.AddDays(-1), 30, null, Now);
        cancelled.Cancel(Now);
        var passed = Interview.Create(Guid.NewGuid(), companyId, apps[2].Id, Guid.NewGuid(), Now.AddDays(-1), 30, null, Now);
        passed.RecordOutcome(InterviewOutcome.Passed, null, Now);
        var failed = Interview.Create(Guid.NewGuid(), companyId, apps[3].Id, Guid.NewGuid(), Now.AddDays(-1), 30, null, Now);
        failed.RecordOutcome(InterviewOutcome.Failed, null, Now);
        var noShow = Interview.Create(Guid.NewGuid(), companyId, apps[4].Id, Guid.NewGuid(), Now.AddDays(-1), 30, null, Now);
        noShow.RecordOutcome(InterviewOutcome.NoShow, null, Now);

        db.Interviews.AddRange(pending, cancelled, passed, failed, noShow);
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.Equal(1, result.Count);
        Assert.Equal(pending.Id, result.Items[0].InterviewId);
    }

    [Fact]
    public async Task HandleAsync_Counts_Rescheduled_Interview_Only_When_New_Time_Is_Today_Or_Past()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 2);
        var apps = AddApplications(db, companyId, vacancy.Id, candidates);

        // Originally future, moved earlier to yesterday — now requires action.
        var movedEarlier = Interview.Create(Guid.NewGuid(), companyId, apps[0].Id, Guid.NewGuid(), Now.AddDays(5), 30, null, Now);
        movedEarlier.UpdateDetails(Guid.NewGuid(), Now.AddDays(-1), 30, null, Now);
        // Originally past, pushed out to next week — no action due yet.
        var movedLater = Interview.Create(Guid.NewGuid(), companyId, apps[1].Id, Guid.NewGuid(), Now.AddDays(-2), 30, null, Now);
        movedLater.UpdateDetails(Guid.NewGuid(), Now.AddDays(7), 30, null, Now);

        db.Interviews.AddRange(movedEarlier, movedLater);
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.Equal(1, result.Count);
        Assert.Equal(movedEarlier.Id, result.Items[0].InterviewId);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 1);
        var (otherVacancy, otherCandidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, otherCompanyId, 1);
        var apps = AddApplications(db, companyId, vacancy.Id, candidates);
        var otherApps = AddApplications(db, otherCompanyId, otherVacancy.Id, otherCandidates);

        db.Interviews.AddRange(
            Interview.Create(Guid.NewGuid(), companyId, apps[0].Id, Guid.NewGuid(), Now.AddDays(-1), 30, null, Now),
            Interview.Create(Guid.NewGuid(), otherCompanyId, otherApps[0].Id, Guid.NewGuid(), Now.AddDays(-1), 30, null, Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.Equal(1, result.Count);
    }

    private static List<Application> AddApplications(RecruitmentDbContext db, Guid companyId, Guid vacancyId, List<Candidate> candidates)
    {
        var apps = candidates
            .Select(c => Application.Create(Guid.NewGuid(), companyId, vacancyId, c.Id, Guid.NewGuid(), null, Now))
            .ToList();
        db.Applications.AddRange(apps);
        return apps;
    }

    private static async Task<GetInterviewsRequiringActionMetricResponse> Handle(RecruitmentDbContext db, Guid companyId)
    {
        var handler = new GetInterviewsRequiringActionMetricHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        return await handler.HandleAsync(new GetInterviewsRequiringActionMetricRequest { CompanyId = companyId }, CancellationToken.None);
    }
}
