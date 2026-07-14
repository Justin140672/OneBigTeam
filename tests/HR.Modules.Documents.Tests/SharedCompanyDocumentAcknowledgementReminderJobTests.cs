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
        FakeClock? clock = null) =>
        new(db,
            new SharedCompanyDocumentAudienceMatcher(db, audienceReader),
            writer,
            taskCreator ?? new FakeTaskCreator(),
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
            "handbook.pdf", 100, "application/pdf", null, null,
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

        await BuildJob(db, audienceReader, writer).ExecuteAsync();

        var reminder = Assert.Single(writer.Written,
            n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementReminder);
        Assert.Equal(companyId, reminder.CompanyId);
        Assert.Equal(employeeId, reminder.EmployeeId);
        Assert.Equal(doc.Id, reminder.SourceEntityId);
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

        await BuildJob(db, audienceReader, writer).ExecuteAsync();

        var overdue = Assert.Single(writer.Written,
            n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementOverdue);
        Assert.Equal(companyId, overdue.CompanyId);
        Assert.Equal(employeeId, overdue.EmployeeId);
        Assert.Equal(doc.Id, overdue.SourceEntityId);
        Assert.Equal(NotificationPriority.High, overdue.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_DueSoon_Reminder_When_Due_Date_Is_Outside_The_Window()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category   = await SeedCategoryAsync(db, companyId);
        await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(10));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();

        await BuildJob(db, audienceReader, writer).ExecuteAsync();

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
                Guid.NewGuid(), companyId, doc.Id, employeeId, doc.VersionNumber, "Statement", null, Now));
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
        await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(2));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();
        var job = BuildJob(db, audienceReader, writer, taskCreator);

        // Run twice — second run sees the existing reminder via ExistsAsync and skips.
        await job.ExecuteAsync();
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
        await SeedDocumentAsync(db, companyId, category.Id, Today.AddDays(2));

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();

        // First run: due date is within the due-soon window relative to FixedUtcNow — establishes
        // both the reminder notification and the task.
        await BuildJob(db, audienceReader, writer, taskCreator).ExecuteAsync();

        Assert.Single(taskCreator.Created, t => t.AssignedEmployeeId == employeeId);
        Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementReminder);

        // Second run: a later clock makes the SAME fixed due date now overdue.
        var laterClock = new FakeClock(FixedUtcNow.AddDays(10));
        await BuildJob(db, audienceReader, writer, taskCreator, laterClock).ExecuteAsync();

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
