using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class InterviewFeedbackServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordFeedbackAsync_Records_Outcome_On_Interview()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.ScheduleInterview(Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var recordedBy = Guid.NewGuid();

        var result = await service(db).RecordFeedbackAsync(
            companyId, interview.Id, recordedBy, "Passed", "Strong technical skills.", CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.Interviews.SingleAsync();
        Assert.Equal(InterviewOutcome.Passed, saved.Outcome);
        Assert.Equal("Strong technical skills.", saved.Notes);

        var savedApplication = await db.Applications.SingleAsync();
        Assert.Equal(ApplicationStatus.Interviewed, savedApplication.Status);
    }

    [Fact]
    public async Task RecordFeedbackAsync_Returns_Validation_Error_For_Unrecognised_Outcome()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Backend Engineer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var result = await service(db).RecordFeedbackAsync(
            companyId, interview.Id, Guid.NewGuid(), "Maybe", null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task RecordFeedbackAsync_Returns_NotFound_When_Interview_Missing()
    {
        await using var db = BuildContext();

        var result = await service(db).RecordFeedbackAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Passed", null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static InterviewFeedbackService service(RecruitmentDbContext db) =>
        new(db, new InterviewOutcomeRecorder(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher()));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
