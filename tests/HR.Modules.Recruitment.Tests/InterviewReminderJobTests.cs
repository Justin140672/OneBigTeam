using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class InterviewReminderJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static InterviewReminderJob BuildJob(RecruitmentDbContext db, FakeNotificationWriter writer) =>
        new(db, writer, new FakeClock(FixedUtcNow));

    private static async Task<(Guid interviewId, Guid interviewerId, Guid companyId)> SeedInterviewAsync(
        RecruitmentDbContext db, DateTimeOffset scheduledAt, InterviewOutcome outcome = InterviewOutcome.Pending)
    {
        var companyId = Guid.NewGuid();
        var interviewerId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, Guid.NewGuid(), null, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, interviewerId, scheduledAt, 30, "Remote", Now);

        if (outcome != InterviewOutcome.Pending)
            interview.RecordOutcome(outcome, null, Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        return (interview.Id, interviewerId, companyId);
    }

    [Fact]
    public async Task ExecuteAsync_Sends_Reminder_For_Interview_Within_Next_Two_Hours()
    {
        await using var db = BuildContext();
        var (interviewId, interviewerId, companyId) = await SeedInterviewAsync(db, Now.AddHours(1));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();

        var reminder = Assert.Single(writer.Written, n => n.Type == NotificationType.InterviewReminder);
        Assert.Equal(companyId, reminder.CompanyId);
        Assert.Equal(interviewerId, reminder.EmployeeId);
        Assert.Equal(interviewId, reminder.SourceEntityId);
        Assert.Equal(NotificationPriority.Normal, reminder.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Duplicate_Reminder_When_Already_Sent()
    {
        await using var db = BuildContext();
        await SeedInterviewAsync(db, Now.AddHours(1));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.InterviewReminder);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Reminder_For_Interview_Outside_Window()
    {
        await using var db = BuildContext();
        await SeedInterviewAsync(db, Now.AddHours(5));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();

        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Reminder_For_Interview_Already_In_The_Past()
    {
        await using var db = BuildContext();
        await SeedInterviewAsync(db, Now.AddHours(-1));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();

        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Reminder_For_Non_Pending_Interview()
    {
        await using var db = BuildContext();
        await SeedInterviewAsync(db, Now.AddHours(1), InterviewOutcome.Passed);

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();

        Assert.Empty(writer.Written);
    }
}
