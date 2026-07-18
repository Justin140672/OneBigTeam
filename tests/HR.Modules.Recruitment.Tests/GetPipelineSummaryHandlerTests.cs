using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetPipelineSummary;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetPipelineSummaryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_All_Funnel_Stages_Zero_Filled_When_No_Applications()
    {
        await using var db = BuildContext();
        var handler = new GetPipelineSummaryHandler(db);

        var result = await handler.HandleAsync(new GetPipelineSummaryRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(
            ["Applied", "Screening", "InterviewScheduled", "Interviewed", "Offered", "Hired"],
            result.Items.Select(i => i.Status).ToArray());
        Assert.All(result.Items, i => Assert.Equal(0, i.ApplicationCount));
    }

    [Fact]
    public async Task HandleAsync_Groups_By_Status_And_Zero_Fills_Stages_With_No_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var (vacancy, candidatePool) = SeedVacancyAndCandidates(db, companyId, 5);

        db.Applications.AddRange(
            CreateApplicationWithStatus(companyId, vacancy.Id, candidatePool[0].Id, ApplicationStatus.Applied),
            CreateApplicationWithStatus(companyId, vacancy.Id, candidatePool[1].Id, ApplicationStatus.Applied),
            CreateApplicationWithStatus(companyId, vacancy.Id, candidatePool[2].Id, ApplicationStatus.Screening),
            CreateApplicationWithStatus(companyId, vacancy.Id, candidatePool[3].Id, ApplicationStatus.Interviewed),
            CreateApplicationWithStatus(companyId, vacancy.Id, candidatePool[4].Id, ApplicationStatus.Hired));
        await db.SaveChangesAsync();

        var handler = new GetPipelineSummaryHandler(db);
        var result = await handler.HandleAsync(new GetPipelineSummaryRequest(companyId), CancellationToken.None);

        var byStatus = result.Items.ToDictionary(i => i.Status, i => i.ApplicationCount);
        Assert.Equal(2, byStatus["Applied"]);
        Assert.Equal(1, byStatus["Screening"]);
        Assert.Equal(0, byStatus["InterviewScheduled"]);
        Assert.Equal(1, byStatus["Interviewed"]);
        Assert.Equal(0, byStatus["Offered"]);
        Assert.Equal(1, byStatus["Hired"]);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Rejected_And_Withdrawn_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var (vacancy, candidatePool) = SeedVacancyAndCandidates(db, companyId, 3);

        db.Applications.AddRange(
            CreateApplicationWithStatus(companyId, vacancy.Id, candidatePool[0].Id, ApplicationStatus.Applied),
            CreateApplicationWithStatus(companyId, vacancy.Id, candidatePool[1].Id, ApplicationStatus.Rejected),
            CreateApplicationWithStatus(companyId, vacancy.Id, candidatePool[2].Id, ApplicationStatus.Withdrawn));
        await db.SaveChangesAsync();

        var handler = new GetPipelineSummaryHandler(db);
        var result = await handler.HandleAsync(new GetPipelineSummaryRequest(companyId), CancellationToken.None);

        Assert.Equal(6, result.Items.Count);
        Assert.DoesNotContain(result.Items, i => i.Status is "Rejected" or "Withdrawn");
        Assert.Equal(1, result.Items.Sum(i => i.ApplicationCount));
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var (vacancy, candidatePool) = SeedVacancyAndCandidates(db, companyId, 1);
        var (otherVacancy, otherCandidatePool) = SeedVacancyAndCandidates(db, otherCompanyId, 1);

        db.Applications.AddRange(
            CreateApplicationWithStatus(companyId, vacancy.Id, candidatePool[0].Id, ApplicationStatus.Applied),
            CreateApplicationWithStatus(otherCompanyId, otherVacancy.Id, otherCandidatePool[0].Id, ApplicationStatus.Applied),
            CreateApplicationWithStatus(otherCompanyId, otherVacancy.Id, otherCandidatePool[0].Id, ApplicationStatus.Applied));
        await db.SaveChangesAsync();

        var handler = new GetPipelineSummaryHandler(db);
        var result = await handler.HandleAsync(new GetPipelineSummaryRequest(companyId), CancellationToken.None);

        var applied = Assert.Single(result.Items, i => i.Status == "Applied");
        Assert.Equal(1, applied.ApplicationCount);
    }

    private static (Vacancy Vacancy, List<Candidate> Candidates) SeedVacancyAndCandidates(
        RecruitmentDbContext db, Guid companyId, int candidateCount)
    {
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Software Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);

        var candidates = new List<Candidate>();
        for (var i = 0; i < candidateCount; i++)
        {
            var candidate = Candidate.Create(
                Guid.NewGuid(), companyId, "First", $"Last{i}", $"candidate{i}.{Guid.NewGuid():N}@example.com", null, null, Now);
            candidates.Add(candidate);
        }
        db.Candidates.AddRange(candidates);

        return (vacancy, candidates);
    }

    internal static Application CreateApplicationWithStatus(
        Guid companyId, Guid vacancyId, Guid candidateId, ApplicationStatus status, DateTimeOffset? appliedAt = null)
    {
        var application = Application.Create(Guid.NewGuid(), companyId, vacancyId, candidateId, null, appliedAt ?? Now);

        switch (status)
        {
            case ApplicationStatus.Applied:
                break;
            case ApplicationStatus.Screening:
                application.MoveToScreening(Now);
                break;
            case ApplicationStatus.InterviewScheduled:
                application.MoveToScreening(Now);
                application.ScheduleInterview(Now);
                break;
            case ApplicationStatus.Interviewed:
                application.MoveToScreening(Now);
                application.ScheduleInterview(Now);
                application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
                break;
            case ApplicationStatus.Offered:
                application.MoveToScreening(Now);
                application.ScheduleInterview(Now);
                application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
                application.Offer(Now);
                break;
            case ApplicationStatus.Hired:
                application.MoveToScreening(Now);
                application.ScheduleInterview(Now);
                application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
                application.Offer(Now);
                application.Hire(Now);
                break;
            case ApplicationStatus.Rejected:
                application.Reject(Now);
                break;
            case ApplicationStatus.Withdrawn:
                application.Withdraw(Now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        return application;
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
