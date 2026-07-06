using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class OutstandingInterviewFeedbackReminderJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static OutstandingInterviewFeedbackReminderJob BuildJob(RecruitmentDbContext db, FakeNotificationWriter writer) =>
        new(db, writer, new FakeClock(FixedUtcNow));

    private static async Task<(Guid interviewId, Guid hiringManagerId, Guid companyId)> SeedInterviewAsync(
        RecruitmentDbContext db, DateTimeOffset scheduledAt, InterviewOutcome outcome = InterviewOutcome.Pending)
    {
        var companyId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, hiringManagerId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), scheduledAt, 30, "Remote", Now);

        if (outcome != InterviewOutcome.Pending)
            interview.RecordOutcome(outcome, null, Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        return (interview.Id, hiringManagerId, companyId);
    }

    [Fact]
    public async Task ExecuteAsync_Notifies_HiringManager_When_Feedback_Overdue()
    {
        await using var db = BuildContext();
        // Interview was 2 days ago -> its +1 day feedback window has passed.
        var (interviewId, hiringManagerId, companyId) = await SeedInterviewAsync(db, Now.AddDays(-2));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();

        var overdue = Assert.Single(writer.Written, n => n.Type == NotificationType.InterviewFeedbackOverdue);
        Assert.Equal(companyId, overdue.CompanyId);
        Assert.Equal(hiringManagerId, overdue.EmployeeId);
        Assert.Equal(interviewId, overdue.SourceEntityId);
        Assert.Equal(NotificationPriority.High, overdue.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Duplicate_Notification_When_Already_Sent()
    {
        await using var db = BuildContext();
        await SeedInterviewAsync(db, Now.AddDays(-2));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.InterviewFeedbackOverdue);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Notify_When_Interview_Was_Within_The_One_Day_Feedback_Window()
    {
        await using var db = BuildContext();
        // Interview happened 12 hours ago -> still within the +1 day feedback window.
        await SeedInterviewAsync(db, Now.AddHours(-12));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();

        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Notify_For_Future_Interview()
    {
        await using var db = BuildContext();
        await SeedInterviewAsync(db, Now.AddDays(3));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();

        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Notify_When_Outcome_Already_Recorded()
    {
        await using var db = BuildContext();
        await SeedInterviewAsync(db, Now.AddDays(-2), InterviewOutcome.Passed);

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();

        Assert.Empty(writer.Written);
    }
}
