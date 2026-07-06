using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UpdateInterview;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class UpdateInterviewHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Updates_Interview_Details()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, "Remote", Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var newInterviewerId = Guid.NewGuid();
        var newScheduledAt = Now.AddDays(4);

        var result = await handler(db).HandleAsync(
            new UpdateInterviewRequest
            {
                CompanyId             = companyId,
                VacancyId             = vacancy.Id,
                ApplicationId         = application.Id,
                InterviewId           = interview.Id,
                InterviewerEmployeeId = newInterviewerId,
                ScheduledAt           = newScheduledAt,
                DurationMinutes       = 60,
                Location              = "Office - Room 3",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newInterviewerId, result.Value!.InterviewerEmployeeId);
        Assert.Equal(newScheduledAt, result.Value.ScheduledAt);
        Assert.Equal(60, result.Value.DurationMinutes);
        Assert.Equal("Office - Room 3", result.Value.Location);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new UpdateInterviewRequest
            {
                CompanyId             = Guid.NewGuid(),
                VacancyId             = Guid.NewGuid(),
                ApplicationId         = Guid.NewGuid(),
                InterviewId           = Guid.NewGuid(),
                InterviewerEmployeeId = Guid.NewGuid(),
                ScheduledAt           = Now.AddDays(3),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Interview_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Backend Engineer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new UpdateInterviewRequest
            {
                CompanyId             = companyId,
                VacancyId             = vacancy.Id,
                ApplicationId         = application.Id,
                InterviewId           = Guid.NewGuid(),
                InterviewerEmployeeId = Guid.NewGuid(),
                ScheduledAt           = Now.AddDays(3),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Outcome_Already_Recorded()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Product Designer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Olivia", "Grant", "olivia.grant@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now);
        interview.RecordOutcome(InterviewOutcome.Passed, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new UpdateInterviewRequest
            {
                CompanyId             = companyId,
                VacancyId             = vacancy.Id,
                ApplicationId         = application.Id,
                InterviewId           = interview.Id,
                InterviewerEmployeeId = Guid.NewGuid(),
                ScheduledAt           = Now.AddDays(3),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    private static UpdateInterviewHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
