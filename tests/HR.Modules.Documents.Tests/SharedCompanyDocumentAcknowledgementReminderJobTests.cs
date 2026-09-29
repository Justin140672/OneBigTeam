using HR.Modules.Tasks.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class SharedCompanyDocumentAcknowledgementReminderJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SharedCompanyDocumentAcknowledgementReminderJob BuildJob(
        DocumentsDbContext db,
        FakeEmployeeAudienceReader audienceReader,
        FakeNotificationWriter writer,
        FakeTaskCreator? taskCreator = null,
        FakeClock? clock = null,
        FakeOpenTaskBySourceEntityReader? openTaskReader = null,
        FakeCompanyAcknowledgementSettingsReader? acknowledgementSettingsReader = null,
        FakeManagerReader? managerReader = null,
        FakeEmployeeNameReader? employeeNameReader = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(db,
            new SharedCompanyDocumentAudienceMatcher(db, audienceReader),
            writer,
            taskCreator ?? new FakeTaskCreator(),
            openTaskReader ?? new FakeOpenTaskBySourceEntityReader(),
            acknowledgementSettingsReader ?? new FakeCompanyAcknowledgementSettingsReader(),
            managerReader ?? new FakeManagerReader(),
            employeeNameReader ?? new FakeEmployeeNameReader(),
            auditPublisher ?? new FakeAuditPublisher(),
            clock ?? new FakeClock(FixedUtcNow));

    private static async Task<CompanyDocumentCategory> SeedCategoryAsync(DocumentsDbContext db, Guid companyId)
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", Now);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static async Task<SharedCompanyDocument> SeedDocumentAsync(
        DocumentsDbContext db,
        Guid companyId,
        Guid categoryId,
        DateOnly? acknowledgementDueDate,
        SharedCompanyDocumentStatus status = SharedCompanyDocumentStatus.Published,
        bool requiresAcknowledgement = true)
    {
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Employee Handbook", null, categoryId, "key/handbook.pdf",
            "handbook.pdf", 100, "application/pdf", null, null, SharedCompanyDocumentReviewFrequency.None, null, null,
            requiresAcknowledgement, acknowledgementDueDate, null, Guid.NewGuid(), Now);

        if (status is SharedCompanyDocumentStatus.Published or SharedCompanyDocumentStatus.Archived)
            doc.Publish(Guid.NewGuid(), Now);

        if (status == SharedCompanyDocumentStatus.Archived)
            doc.Archive(Guid.NewGuid(), "Superseded", Now);

        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    [Fact]
    public async Task ExecuteAsync_Sends_DueSoon_Reminder_When_Due_Date_Is_Within_Window_And_Not_Acknowledged()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        var doc        = await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(2));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();

        await BuildJob(db, audienceReader, writer, taskCreator).ExecuteAsync();

        var task = Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
        var reminder = Assert.Single(writer.Written,
            n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementReminder);
        Assert.Equal(companyId, reminder.CompanyId);
        Assert.Equal(employeeId, reminder.EmployeeId);
        Assert.Equal(task.Id, reminder.SourceEntityId);
        Assert.Equal(NotificationPriority.Normal, reminder.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Sends_Overdue_Reminder_When_Due_Date_Has_Passed_And_Not_Acknowledged()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        var doc        = await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(-1));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();

        await BuildJob(db, audienceReader, writer, taskCreator).ExecuteAsync();

        var task = Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
        var overdue = Assert.Single(writer.Written,
            n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementOverdue);
        Assert.Equal(companyId, overdue.CompanyId);
        Assert.Equal(employeeId, overdue.EmployeeId);
        Assert.Equal(task.Id, overdue.SourceEntityId);
        Assert.Equal(NotificationPriority.High, overdue.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Task_And_Sends_Immediate_Reminder_For_NeverEngaged_Employee_Even_Outside_The_Window()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        var doc        = await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(10));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();

        await BuildJob(db, audienceReader, writer, taskCreator).ExecuteAsync();

        var createdTask = Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
        var reminder = Assert.Single(writer.Written,
            n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementReminder);
        Assert.Equal(createdTask.Id, reminder.SourceEntityId);
        Assert.Equal(NotificationPriority.Normal, reminder.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Recreate_A_Task_On_A_Later_Run_For_An_Employee_Already_Engaged_Outside_The_Window()
    {
        // Regression guard for the duplicate-task hole: once a never-engaged employee outside the
        // window has been handled by one run, a second run (still outside the window) must not
        // create a second task or send a second reminder.
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        var doc         = await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(10));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();
        var openTaskReader = new FakeOpenTaskBySourceEntityReader();
        var job = BuildJob(db, audienceReader, writer, taskCreator, openTaskReader: openTaskReader);

        await job.ExecuteAsync();

        var firstRunTask = Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
        openTaskReader.AddOpenTaskForAssignee(doc.Id, employeeId, TaskActionType.Acknowledge, firstRunTask.Id);

        await job.ExecuteAsync();

        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
        Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementReminder);
    }

    [Fact]
    public async Task ExecuteAsync_Assigns_Task_To_An_Employee_Who_Enters_The_Audience_Between_Runs()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var existingEmployee = Guid.NewGuid();
        var movedEmployee    = Guid.NewGuid();
        var category         = await SeedCategoryAsync(db, companyId);
        var doc               = await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(10));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [existingEmployee] };
        var writer = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();
        var openTaskReader = new FakeOpenTaskBySourceEntityReader();
        var job = BuildJob(db, audienceReader, writer, taskCreator, openTaskReader: openTaskReader);

        await job.ExecuteAsync();

        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == existingEmployee);
        Assert.DoesNotContain(taskCreator.Created, t => t.AssignedEmployeeId == movedEmployee);

        var existingEmployeeTask = taskCreator.Created.Single(t => t.AssignedEmployeeId == existingEmployee);
        openTaskReader.AddOpenTaskForAssignee(doc.Id, existingEmployee, TaskActionType.Acknowledge, existingEmployeeTask.Id);

        audienceReader.EligibleEmployeeIds = [existingEmployee, movedEmployee];

        await job.ExecuteAsync();

        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == movedEmployee);
        var newHireTask = taskCreator.Created.Single(t => t.AssignedEmployeeId == movedEmployee);
        Assert.Equal(doc.Id, newHireTask.SourceEntityId);
        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == existingEmployee);

        openTaskReader.AddOpenTaskForAssignee(doc.Id, movedEmployee, TaskActionType.Acknowledge, newHireTask.Id);

        await job.ExecuteAsync();

        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == existingEmployee);
        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == movedEmployee);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Delete_A_Completed_Acknowledgement_For_An_Employee_Who_Has_Since_Left_The_Audience()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        var doc        = await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(10));

        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(
                Guid.NewGuid(), companyId, doc.Id, employeeId, doc.VersionNumber, "Statement", null, true, Now));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [] };
        var writer = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();

        await BuildJob(db, audienceReader, writer, taskCreator).ExecuteAsync();

        var acknowledgement = await db.SharedCompanyDocumentAcknowledgements
            .SingleAsync(a => a.SharedCompanyDocumentId == doc.Id && a.EmployeeId == employeeId);
        Assert.Equal(doc.VersionNumber, acknowledgement.VersionNumber);
        Assert.Empty(taskCreator.Created);
        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Anything_To_Employee_Who_Already_Acknowledged_Current_Version()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        var doc        = await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(2));

        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(
                Guid.NewGuid(), companyId, doc.Id, employeeId, doc.VersionNumber, "Statement", null, true, Now));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();

        await BuildJob(db, audienceReader, writer).ExecuteAsync();

        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Anything_For_A_Draft_Document()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(2), SharedCompanyDocumentStatus.Draft);

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();

        await BuildJob(db, audienceReader, writer).ExecuteAsync();

        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Anything_For_An_Archived_Document()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(2), SharedCompanyDocumentStatus.Archived);

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();

        await BuildJob(db, audienceReader, writer).ExecuteAsync();

        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Duplicate_DueSoon_Reminder_When_Executed_Twice()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        var doc         = await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(2));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();
        var openTaskReader = new FakeOpenTaskBySourceEntityReader();
        var job = BuildJob(db, audienceReader, writer, taskCreator, openTaskReader: openTaskReader);

        await job.ExecuteAsync();
        var firstRunTask = taskCreator.Created.Single(t => t.AssignedEmployeeId == employeeId);
        openTaskReader.AddOpenTaskForAssignee(doc.Id, employeeId, TaskActionType.Acknowledge, firstRunTask.Id);
        await job.ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementReminder);
        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Acknowledgement_Task_For_NeverEngaged_Employee_In_DueSoon_Window()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        var doc        = await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(2));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();

        await BuildJob(db, audienceReader, writer, taskCreator).ExecuteAsync();

        var task = Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
        Assert.Equal("Acknowledge: Employee Handbook (v1)", task.Title);
        Assert.Equal(TaskActionType.Acknowledge, task.ActionType);
        Assert.Equal(TaskSource.Document, task.Source);
        Assert.Equal(doc.Id, task.SourceEntityId);

        Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementReminder);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Acknowledgement_Task_For_NeverEngaged_Employee_Who_Is_Already_Overdue()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        var doc        = await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(-1));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();

        await BuildJob(db, audienceReader, writer, taskCreator).ExecuteAsync();

        var task = Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
        Assert.Equal("Acknowledge: Employee Handbook (v1)", task.Title);
        Assert.Equal(doc.Id, task.SourceEntityId);

        Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementOverdue);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Create_A_Second_Task_When_A_DueSoon_Recipient_Later_Becomes_Overdue()
    {
        // Proves the coordination fix: once an employee has been engaged via either notification
        // type for this document's current version, a later run of the job (even one that now
        // sees the same fixed due date as overdue) must not create a second acknowledgement task.
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        var doc         = await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(2));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();
        var openTaskReader = new FakeOpenTaskBySourceEntityReader();

        await BuildJob(db, audienceReader, writer, taskCreator, openTaskReader: openTaskReader).ExecuteAsync();

        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
        Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementReminder);

        var firstRunTask = taskCreator.Created.Single(t => t.AssignedEmployeeId == employeeId);
        openTaskReader.AddOpenTaskForAssignee(doc.Id, employeeId, TaskActionType.Acknowledge, firstRunTask.Id);

        var laterClock = new FakeClock(FixedUtcNow.AddDays(10));
        await BuildJob(db, audienceReader, writer, taskCreator, laterClock, openTaskReader).ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementOverdue);
        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Anything_To_An_Employee_Excluded_From_The_Audience()
    {
        await using var db = BuildContext();
        var companyId            = Guid.NewGuid();
        var excludedEmployeeId   = Guid.NewGuid();
        var category             = await SeedCategoryAsync(db, companyId);
        await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(2));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [] };
        var writer = new FakeNotificationWriter();

        await BuildJob(db, audienceReader, writer).ExecuteAsync();

        Assert.DoesNotContain(writer.Written, n => n.EmployeeId == excludedEmployeeId);
        Assert.Empty(writer.Written);
    }
}
