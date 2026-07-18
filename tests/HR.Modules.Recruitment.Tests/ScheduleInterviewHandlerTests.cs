using HR.Infrastructure.Abstractions;
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
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
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
            Guid.NewGuid(),
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
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
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
            Guid.NewGuid(),
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
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Application_Already_Hired()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
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
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Creates_Preparation_And_Feedback_Tasks_For_Interviewer()
    {
        await using var db = BuildContext();
        var taskCreator = new FakeTaskCreator();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var interviewerId = Guid.NewGuid();
        var scheduledBy = Guid.NewGuid();
        var scheduledAt = Now.AddDays(3);

        var result = await handler(db, taskCreator).HandleAsync(
            new ScheduleInterviewRequest
            {
                CompanyId             = companyId,
                VacancyId             = vacancy.Id,
                ApplicationId         = application.Id,
                InterviewerEmployeeId = interviewerId,
                ScheduledAt           = scheduledAt,
            },
            scheduledBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, taskCreator.Created.Count);

        var prepTask = taskCreator.Created.Single(t => t.ActionType == TaskActionType.Review);
        Assert.Equal(companyId, prepTask.CompanyId);
        Assert.Equal(interviewerId, prepTask.AssignedEmployeeId);
        Assert.Equal(result.Value!.Id, prepTask.SourceEntityId);
        Assert.Equal(TaskSource.Recruitment, prepTask.Source);
        Assert.Contains("Emma Clarke", prepTask.Title);
        Assert.Equal(DateOnly.FromDateTime(scheduledAt.UtcDateTime), prepTask.DueDate);

        var feedbackTask = taskCreator.Created.Single(t => t.ActionType == TaskActionType.Complete);
        Assert.Equal(interviewerId, feedbackTask.AssignedEmployeeId);
        Assert.Equal(result.Value.Id, feedbackTask.SourceEntityId);
        Assert.Equal(TaskSource.Recruitment, feedbackTask.Source);
        Assert.Contains("Emma Clarke", feedbackTask.Title);
        Assert.Equal(DateOnly.FromDateTime(scheduledAt.UtcDateTime).AddDays(1), feedbackTask.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Create_Tasks_When_Application_Missing()
    {
        await using var db = BuildContext();
        var taskCreator = new FakeTaskCreator();

        await handler(db, taskCreator).HandleAsync(
            new ScheduleInterviewRequest
            {
                CompanyId             = Guid.NewGuid(),
                VacancyId             = Guid.NewGuid(),
                ApplicationId         = Guid.NewGuid(),
                InterviewerEmployeeId = Guid.NewGuid(),
                ScheduledAt           = Now.AddDays(3),
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task HandleAsync_Writes_Notification_To_Interviewer()
    {
        await using var db = BuildContext();
        var notificationWriter = new FakeNotificationWriter();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var interviewerId = Guid.NewGuid();
        var scheduledAt = Now.AddDays(3);

        var result = await handler(db, notificationWriter: notificationWriter).HandleAsync(
            new ScheduleInterviewRequest
            {
                CompanyId             = companyId,
                VacancyId             = vacancy.Id,
                ApplicationId         = application.Id,
                InterviewerEmployeeId = interviewerId,
                ScheduledAt           = scheduledAt,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var notification = Assert.Single(notificationWriter.Written);
        Assert.Equal(interviewerId, notification.EmployeeId);
        Assert.Equal(companyId, notification.CompanyId);
        Assert.Equal(NotificationType.InterviewScheduled, notification.Type);
        Assert.Equal(result.Value!.Id, notification.SourceEntityId);
        Assert.Contains("Emma Clarke", notification.Body);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Write_Notification_When_Application_Missing()
    {
        await using var db = BuildContext();
        var notificationWriter = new FakeNotificationWriter();

        await handler(db, notificationWriter: notificationWriter).HandleAsync(
            new ScheduleInterviewRequest
            {
                CompanyId             = Guid.NewGuid(),
                VacancyId             = Guid.NewGuid(),
                ApplicationId         = Guid.NewGuid(),
                InterviewerEmployeeId = Guid.NewGuid(),
                ScheduledAt           = Now.AddDays(3),
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Empty(notificationWriter.Written);
    }

    [Fact]
    public async Task HandleAsync_Uses_PositionProfile_Title_In_Task_And_Notification_Text_When_AdvertTitle_Is_Null()
    {
        await using var db = BuildContext();
        var taskCreator = new FakeTaskCreator();
        var notificationWriter = new FakeNotificationWriter();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Position Profile Title", null, null, true, null, null),
        };

        var result = await handler(db, taskCreator, notificationWriter, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new ScheduleInterviewRequest
            {
                CompanyId             = companyId,
                VacancyId             = vacancy.Id,
                ApplicationId         = application.Id,
                InterviewerEmployeeId = Guid.NewGuid(),
                ScheduledAt           = Now.AddDays(3),
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var prepTask = taskCreator.Created.Single(t => t.ActionType == TaskActionType.Review);
        Assert.Contains("Position Profile Title", prepTask.Description);
        var notification = Assert.Single(notificationWriter.Written);
        Assert.Contains("Position Profile Title", notification.Body);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_This_Vacancy_In_Task_Text_When_No_AdvertTitle_Or_PositionProfile()
    {
        await using var db = BuildContext();
        var taskCreator = new FakeTaskCreator();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        // No summaries dictionary supplied — simulates the linked profile no longer being resolvable.
        var result = await handler(db, taskCreator).HandleAsync(
            new ScheduleInterviewRequest
            {
                CompanyId             = companyId,
                VacancyId             = vacancy.Id,
                ApplicationId         = application.Id,
                InterviewerEmployeeId = Guid.NewGuid(),
                ScheduledAt           = Now.AddDays(3),
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var prepTask = taskCreator.Created.Single(t => t.ActionType == TaskActionType.Review);
        Assert.Contains("this vacancy", prepTask.Description);
    }

    private static ScheduleInterviewHandler handler(
        RecruitmentDbContext db,
        FakeTaskCreator? taskCreator = null,
        FakeNotificationWriter? notificationWriter = null,
        IPositionProfileReader? positionProfileReader = null) =>
        new(db, taskCreator ?? new FakeTaskCreator(), notificationWriter ?? new FakeNotificationWriter(), new FakeClock(FixedUtcNow), positionProfileReader ?? new FakePositionProfileReader());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
