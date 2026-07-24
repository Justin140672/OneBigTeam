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
        // A never-engaged, eligible-and-outstanding employee gets their task and first notice on
        // this run regardless of how far the due date is — this is the reconciliation behaviour:
        // it's what lets an employee who is newly brought into the audience (department/location/
        // position change, new hire, or an audience-rule edit) get assigned promptly rather than
        // waiting until the due-soon window. The due-soon *window* still governs re-engagement
        // nagging for employees who were already engaged earlier — see the "does not duplicate"
        // and "already engaged" tests below.
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

        // The real IOpenTaskBySourceEntityReader implementation queries the live Tasks database,
        // so a task created on the first run is naturally visible to the second run's lookup —
        // the fake needs this wired manually to reproduce that continuity.
        var firstRunTask = Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
        openTaskReader.AddOpenTaskForAssignee(doc.Id, employeeId, TaskActionType.Acknowledge, firstRunTask.Id);

        await job.ExecuteAsync();

        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
        Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementReminder);
    }

    [Fact]
    public async Task ExecuteAsync_Assigns_Task_To_An_Employee_Who_Enters_The_Audience_Between_Runs()
    {
        // Simulates an employee whose department/location/position change brings them into the
        // audience after an earlier run already processed everyone else — the reconciliation job
        // picks them up on the very next run, without needing to re-touch anyone already handled.
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

        // Wire the first run's task into the fake reader so the second run sees existingEmployee
        // as already engaged — mirrors the real DB-backed reader's natural continuity.
        var existingEmployeeTask = taskCreator.Created.Single(t => t.AssignedEmployeeId == existingEmployee);
        openTaskReader.AddOpenTaskForAssignee(doc.Id, existingEmployee, TaskActionType.Acknowledge, existingEmployeeTask.Id);

        // The employee's department change lands — they now match the audience.
        audienceReader.EligibleEmployeeIds = [existingEmployee, movedEmployee];

        await job.ExecuteAsync();

        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == movedEmployee);
        var newHireTask = taskCreator.Created.Single(t => t.AssignedEmployeeId == movedEmployee);
        Assert.Equal(doc.Id, newHireTask.SourceEntityId);
        // The existing employee is untouched by the second run — still exactly one task for them.
        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == existingEmployee);

        // Wire the second run's new task for movedEmployee before the third run.
        openTaskReader.AddOpenTaskForAssignee(doc.Id, movedEmployee, TaskActionType.Acknowledge, newHireTask.Id);

        await job.ExecuteAsync();

        // A third run doesn't duplicate anything for either employee.
        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == existingEmployee);
        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == movedEmployee);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Delete_A_Completed_Acknowledgement_For_An_Employee_Who_Has_Since_Left_The_Audience()
    {
        // "Employees leaving the audience lose normal access, but completed acknowledgement
        // history is never deleted" — the job has no delete/remove statement anywhere, so this is
        // really just confirming that guarantee explicitly for the case that matters here: an
        // employee acknowledges, then a department/location/position change takes them out of the
        // audience, and a later run must leave their historical acknowledgement row untouched.
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

        // Run twice — second run sees the existing reminder (still within the configured interval)
        // and skips it. Wire the first run's task into the fake reader so the second run correctly
        // sees the employee as already engaged, same as the real DB-backed reader would.
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
        // Stands in for an employee added to the audience after Publish/UploadSharedCompanyDocumentVersion
        // already ran their one-time task-creation loop (new hire, audience-rule change, etc) — no prior
        // notification is seeded for them.
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

        // First run: due date is within the due-soon window relative to FixedUtcNow — establishes
        // both the reminder notification and the task.
        await BuildJob(db, audienceReader, writer, taskCreator, openTaskReader: openTaskReader).ExecuteAsync();

        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
        Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementReminder);

        // Wire the first run's task into the fake reader (same as the real DB-backed reader would
        // naturally see it) so the second run correctly treats the employee as already engaged.
        var firstRunTask = taskCreator.Created.Single(t => t.AssignedEmployeeId == employeeId);
        openTaskReader.AddOpenTaskForAssignee(doc.Id, employeeId, TaskActionType.Acknowledge, firstRunTask.Id);

        // Second run: a later clock makes the SAME fixed due date now overdue.
        var laterClock = new FakeClock(FixedUtcNow.AddDays(10));
        await BuildJob(db, audienceReader, writer, taskCreator, laterClock, openTaskReader).ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementOverdue);
        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Anything_To_An_Employee_Excluded_From_The_Audience()
    {
        // Documentation case: archived/inactive employees are excluded upstream by
        // IEmployeeAudienceReader.GetEligibleEmployeeIdsAsync, which this job trusts completely —
        // simply not adding an employee to EligibleEmployeeIds reproduces that exclusion here,
        // even though the job itself has no separate "is active" check of its own.
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
