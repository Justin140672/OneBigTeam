using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// TEST-005. Failure-safety / idempotency / tenant-isolation hardening for
/// <see cref="SharedCompanyDocumentAcknowledgementReminderJob"/>, complementing the behavioural
/// coverage in <see cref="SharedCompanyDocumentAcknowledgementReminderJobTests"/>.
/// Note: the job takes no <c>ILogger</c>, so there is no "PII not logged" assertion to make here —
/// the only free-text it produces is notification/audit content, which is covered below.
/// </summary>
public class SharedCompanyDocumentAcknowledgementReminderJobHardeningTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);

    private static DocumentsDbContext BuildContext(string? name = null) =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
            .Options);

    private static SharedCompanyDocumentAcknowledgementReminderJob BuildJob(
        DocumentsDbContext db,
        FakeEmployeeAudienceReader audienceReader,
        INotificationWriter writer,
        FakeTaskCreator taskCreator,
        FakeOpenTaskBySourceEntityReader openTaskReader,
        IAuditEventPublisher auditPublisher,
        FakeManagerReader? managerReader = null,
        FakeEmployeeNameReader? employeeNameReader = null,
        FakeClock? clock = null) =>
        new(db,
            new SharedCompanyDocumentAudienceMatcher(db, audienceReader),
            writer,
            taskCreator,
            openTaskReader,
            new FakeCompanyAcknowledgementSettingsReader(reminderIntervalDays: 3),
            managerReader ?? new FakeManagerReader(),
            employeeNameReader ?? new FakeEmployeeNameReader(),
            auditPublisher,
            clock ?? new FakeClock(FixedUtcNow));

    private static async Task<SharedCompanyDocument> SeedPublishedDocAsync(
        DocumentsDbContext db, Guid companyId, DateOnly dueDate)
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", Now);
        db.CompanyDocumentCategories.Add(category);

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Employee Handbook", null, category.Id, "key/handbook.pdf",
            "handbook.pdf", 100, "application/pdf", null, null, SharedCompanyDocumentReviewFrequency.None, null, null,
            true, dueDate, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    [Fact]
    public async Task First_Run_Sends_One_Reminder_And_Creates_One_Task_With_Tenant_Context_On_Every_Record()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId, Today.AddDays(2));

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();
        var audit = new FakeAuditPublisher();

        await BuildJob(db, audience, writer, tasks, new FakeOpenTaskBySourceEntityReader(), audit).ExecuteAsync();

        var notification = Assert.Single(writer.Written);
        Assert.Equal(companyId, notification.CompanyId);
        var task = Assert.Single(tasks.Created);
        Assert.Equal(companyId, task.CompanyId);
        var reminderEvent = Assert.Single(audit.Published.OfType<SharedCompanyDocumentReminderSentAuditEvent>());
        Assert.Equal(companyId, reminderEvent.CompanyId);
    }

    [Fact]
    public async Task Immediate_Duplicate_Run_Sends_No_Second_Reminder_Task_Or_Audit_Event()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId, Today.AddDays(2));

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();
        var audit = new FakeAuditPublisher();
        var openTasks = new FakeOpenTaskBySourceEntityReader();
        var job = BuildJob(db, audience, writer, tasks, openTasks, audit);

        await job.ExecuteAsync();
        var firstTask = tasks.Created.Single();
        openTasks.AddOpenTaskForAssignee(doc.Id, employeeId, TaskActionType.Acknowledge, firstTask.Id);
        await job.ExecuteAsync();

        Assert.Single(writer.Written);
        Assert.Single(tasks.Created);
        Assert.Single(audit.Published.OfType<SharedCompanyDocumentReminderSentAuditEvent>());
    }

    [Fact]
    public async Task Retry_After_Notification_Write_Fails_Mid_Run_Completes_Cleanly_On_The_Next_Run()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId, Today.AddDays(2));

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var fakeWriter = new FakeNotificationWriter();
        var faultyWriter = new FaultInjectingNotificationWriter(fakeWriter) { FailNextWrites = 1 };
        var tasks = new FakeTaskCreator();
        var audit = new FakeAuditPublisher();
        var openTasks = new FakeOpenTaskBySourceEntityReader();
        var job = BuildJob(db, audience, faultyWriter, tasks, openTasks, audit);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync());

        // Nothing recorded, no audit event — the task may already exist, which is fine (it is reused).
        Assert.Empty(fakeWriter.Written);
        Assert.Empty(audit.Published);
        var createdTask = tasks.Created.Single();
        openTasks.AddOpenTaskForAssignee(doc.Id, employeeId, TaskActionType.Acknowledge, createdTask.Id);

        await job.ExecuteAsync();

        Assert.Single(fakeWriter.Written);
        Assert.Single(tasks.Created); // task reused, not duplicated
        Assert.Single(audit.Published.OfType<SharedCompanyDocumentReminderSentAuditEvent>());
    }

    [Fact]
    public async Task Retry_After_Notification_Delivered_But_Before_Audit_Recorded_Does_Not_Send_A_Second_Notification()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId, Today.AddDays(2));

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();
        var faultyAudit = new FaultInjectingAuditPublisher { FailNextPublishes = 1 };
        var openTasks = new FakeOpenTaskBySourceEntityReader();
        var job = BuildJob(db, audience, writer, tasks, openTasks, faultyAudit);

        // First run: notification is written, then the audit publish throws.
        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync());
        Assert.Single(writer.Written);

        var createdTask = tasks.Created.Single();
        openTasks.AddOpenTaskForAssignee(doc.Id, employeeId, TaskActionType.Acknowledge, createdTask.Id);

        // Second run: the interval has not elapsed, so no second email — even though the audit
        // event for the first send was lost.
        await job.ExecuteAsync();

        Assert.Single(writer.Written);
        Assert.Single(tasks.Created);
    }

    [Fact]
    public async Task Missing_Employee_And_Missing_Manager_Records_Cause_The_Job_To_Skip_Gracefully()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var ghostEmployeeId = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId, Today.AddDays(-1)); // overdue -> escalation path

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [ghostEmployeeId] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();
        var audit = new FakeAuditPublisher();

        // managerReader returns null (no manager record) and employeeNameReader knows no names.
        var ex = await Record.ExceptionAsync(() =>
            BuildJob(db, audience, writer, tasks, new FakeOpenTaskBySourceEntityReader(), audit,
                managerReader: new FakeManagerReader(managerId: null),
                employeeNameReader: new FakeEmployeeNameReader()).ExecuteAsync());

        Assert.Null(ex);
        // The employee still gets their overdue reminder; there is simply no manager to escalate to.
        Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentAcknowledgementOverdue);
        Assert.DoesNotContain(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentManagerEscalation);
    }

    [Fact]
    public async Task Escalation_Uses_Unknown_Placeholder_When_Report_Name_Is_Missing_And_Never_Logs_An_Email_Address()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId, Today.AddDays(-1));

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();
        var audit = new FakeAuditPublisher();

        await BuildJob(db, audience, writer, tasks, new FakeOpenTaskBySourceEntityReader(), audit,
            managerReader: new FakeManagerReader(managerId),
            employeeNameReader: new FakeEmployeeNameReader()).ExecuteAsync();

        var escalation = Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentManagerEscalation);
        Assert.Equal(companyId, escalation.CompanyId);
        Assert.Equal(managerId, escalation.EmployeeId);
        Assert.Contains("Unknown", escalation.Body);
        var escalationEvent = Assert.Single(audit.Published.OfType<SharedCompanyDocumentManagerEscalationSentAuditEvent>());
        Assert.Equal(companyId, escalationEvent.CompanyId);
    }

    [Fact]
    public async Task Manager_Escalation_Is_Not_Duplicated_When_The_Job_Runs_Twice_Within_The_Interval()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId, Today.AddDays(-1));

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();
        var audit = new FakeAuditPublisher();
        var openTasks = new FakeOpenTaskBySourceEntityReader();
        var job = BuildJob(db, audience, writer, tasks, openTasks, audit,
            managerReader: new FakeManagerReader(managerId),
            employeeNameReader: new FakeEmployeeNameReader(new Dictionary<Guid, string> { [employeeId] = "Dana Scully" }));

        await job.ExecuteAsync();
        var firstTask = tasks.Created.Single();
        openTasks.AddOpenTaskForAssignee(doc.Id, employeeId, TaskActionType.Acknowledge, firstTask.Id);
        await job.ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.SharedCompanyDocumentManagerEscalation);
        Assert.Single(audit.Published.OfType<SharedCompanyDocumentManagerEscalationSentAuditEvent>());
    }

    [Fact]
    public async Task Each_Company_Only_Receives_Notifications_Tasks_And_Audit_Events_Scoped_To_Its_Own_Tenant()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var docA = await SeedPublishedDocAsync(db, companyA, Today.AddDays(2));
        var docB = await SeedPublishedDocAsync(db, companyB, Today.AddDays(2));

        // A single audience reader returns each document's own eligible employee only.
        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeA, employeeB] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();
        var audit = new FakeAuditPublisher();

        await BuildJob(db, audience, writer, tasks, new FakeOpenTaskBySourceEntityReader(), audit).ExecuteAsync();

        Assert.All(writer.Written, n => Assert.Contains(n.CompanyId, new[] { companyA, companyB }));
        Assert.All(tasks.Created, t => Assert.Contains(t.CompanyId, new[] { companyA, companyB }));
        // Every notification's company matches the document it was raised for.
        foreach (var n in writer.Written)
        {
            var expectedCompany = tasks.Created.Single(t => t.Id == n.SourceEntityId).CompanyId;
            Assert.Equal(expectedCompany, n.CompanyId);
        }
        Assert.All(audit.Published.OfType<SharedCompanyDocumentReminderSentAuditEvent>(),
            e => Assert.Contains(e.CompanyId, new[] { companyA, companyB }));
    }

    [Fact]
    public async Task Deleted_Document_Between_Runs_Simply_Stops_Producing_Reminders_Without_Throwing()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        Guid docId;

        await using (var seed = BuildContext(dbName))
            docId = (await SeedPublishedDocAsync(seed, companyId, Today.AddDays(2))).Id;

        await using var db = BuildContext(dbName);
        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();
        var audit = new FakeAuditPublisher();
        var openTasks = new FakeOpenTaskBySourceEntityReader();
        var job = BuildJob(db, audience, writer, tasks, openTasks, audit);

        await job.ExecuteAsync();
        Assert.Single(writer.Written);

        db.SharedCompanyDocuments.RemoveRange(db.SharedCompanyDocuments);
        await db.SaveChangesAsync();

        var ex = await Record.ExceptionAsync(() => job.ExecuteAsync());
        Assert.Null(ex);
        Assert.Single(writer.Written);
    }
}
