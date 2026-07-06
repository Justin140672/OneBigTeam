using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ScheduleInterview;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class ScheduleInterviewHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Creates_Interview_And_Moves_Application_To_InterviewScheduled()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var interviewerId = Guid.NewGuid();
        var scheduledAt = Now.AddDays(3);

        var result = await handler(db).HandleAsync(
            new ScheduleInterviewRequest
            {
                CompanyId             = companyId,
                VacancyId             = vacancy.Id,
                ApplicationId         = application.Id,
                InterviewerEmployeeId = interviewerId,
                ScheduledAt           = scheduledAt,
                DurationMinutes       = 30,
                Location              = "Remote",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(interviewerId, result.Value!.InterviewerEmployeeId);
        Assert.Equal(scheduledAt, result.Value.ScheduledAt);
        Assert.Equal(InterviewOutcome.Pending, result.Value.Outcome);

        var savedApplication = await db.Applications.SingleAsync();
        Assert.Equal(ApplicationStatus.InterviewScheduled, savedApplication.Status);
    }

    [Fact]
    public async Task HandleAsync_Allows_Additional_Round_When_Already_InterviewScheduled()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Backend Engineer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new ScheduleInterviewRequest
            {
                CompanyId             = companyId,
                VacancyId             = vacancy.Id,
                ApplicationId         = application.Id,
                InterviewerEmployeeId = Guid.NewGuid(),
                ScheduledAt           = Now.AddDays(5),
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var interviewCount = await db.Interviews.CountAsync(i => i.ApplicationId == application.Id);
        Assert.Equal(1, interviewCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new ScheduleInterviewRequest
            {
                CompanyId             = Guid.NewGuid(),
                VacancyId             = Guid.NewGuid(),
                ApplicationId         = Guid.NewGuid(),
                InterviewerEmployeeId = Guid.NewGuid(),
                ScheduledAt           = Now.AddDays(3),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Application_Already_Hired()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Product Designer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Olivia", "Grant", "olivia.grant@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
        application.Hire(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new ScheduleInterviewRequest
            {
                CompanyId             = companyId,
                VacancyId             = vacancy.Id,
                ApplicationId         = application.Id,
                InterviewerEmployeeId = Guid.NewGuid(),
                ScheduledAt           = Now.AddDays(3),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    private static ScheduleInterviewHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
