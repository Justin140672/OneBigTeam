using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ReissueSharedCompanyDocumentAcknowledgement;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// TEST-005. Idempotency / tenant-isolation for the "reissue acknowledgement request" HR action
/// and its interplay with <see cref="SharedCompanyDocumentAcknowledgementReminderJob"/>, which
/// re-picks the outstanding acknowledgements it creates.
/// </summary>
public class ReissueSharedCompanyDocumentAcknowledgementIdempotencyTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<SharedCompanyDocument> SeedPublishedDocAsync(DocumentsDbContext db, Guid companyId)
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", Now);
        db.CompanyDocumentCategories.Add(category);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Employee Handbook", null, category.Id, "key/handbook.pdf",
            "handbook.pdf", 100, "application/pdf", null, null, SharedCompanyDocumentReviewFrequency.None, null, null,
            true, Today.AddDays(5), null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    private static ReissueSharedCompanyDocumentAcknowledgementHandler BuildHandler(
        DocumentsDbContext db,
        FakeEmployeeAudienceReader audience,
        INotificationWriter writer,
        FakeTaskCreator tasks,
        FakeOpenTaskBySourceEntityReader openTasks) =>
        new(db,
            new SharedCompanyDocumentAudienceMatcher(db, audience),
            writer,
            tasks,
            openTasks,
            new FakeClock(FixedUtcNow));

    private static SharedCompanyDocumentAcknowledgementReminderJob BuildJob(
        DocumentsDbContext db,
        FakeEmployeeAudienceReader audience,
        INotificationWriter writer,
        FakeTaskCreator tasks,
        FakeOpenTaskBySourceEntityReader openTasks) =>
        new(db,
            new SharedCompanyDocumentAudienceMatcher(db, audience),
            writer,
            tasks,
            openTasks,
            new FakeCompanyAcknowledgementSettingsReader(reminderIntervalDays: 3),
            new FakeManagerReader(),
            new FakeEmployeeNameReader(),
            new FakeAuditPublisher(),
            new FakeClock(FixedUtcNow));

    [Fact]
    public async Task First_Reissue_Notifies_Every_Outstanding_Employee_Once_With_Tenant_Context()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var e1 = Guid.NewGuid();
        var e2 = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId);

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [e1, e2] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();

        var result = await BuildHandler(db, audience, writer, tasks, new FakeOpenTaskBySourceEntityReader())
            .HandleAsync(new ReissueSharedCompanyDocumentAcknowledgementRequest(companyId, doc.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.EmployeesNotifiedCount);
        Assert.Equal(2, writer.Written.Count);
        Assert.All(writer.Written, n => Assert.Equal(companyId, n.CompanyId));
        Assert.All(tasks.Created, t => Assert.Equal(companyId, t.CompanyId));
    }

    [Fact]
    public async Task Reissue_Skips_Employees_Who_Already_Acknowledged_The_Current_Version()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var acked = Guid.NewGuid();
        var outstanding = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId);
        db.SharedCompanyDocumentAcknowledgements.Add(SharedCompanyDocumentAcknowledgement.Create(
            Guid.NewGuid(), companyId, doc.Id, acked, doc.VersionNumber, "Statement", null, true, Now));
        await db.SaveChangesAsync();

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [acked, outstanding] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();

        var result = await BuildHandler(db, audience, writer, tasks, new FakeOpenTaskBySourceEntityReader())
            .HandleAsync(new ReissueSharedCompanyDocumentAcknowledgementRequest(companyId, doc.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(1, result.Value!.EmployeesNotifiedCount);
        Assert.Single(writer.Written, n => n.EmployeeId == outstanding);
        Assert.DoesNotContain(writer.Written, n => n.EmployeeId == acked);
    }

    [Fact]
    public async Task Reissue_Reuses_An_Existing_Open_Acknowledge_Task_Rather_Than_Creating_A_Duplicate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId);

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();
        var openTasks = new FakeOpenTaskBySourceEntityReader();
        var existingTaskId = Guid.NewGuid();
        openTasks.AddOpenTaskForAssignee(doc.Id, employeeId, TaskActionType.Acknowledge, existingTaskId);

        await BuildHandler(db, audience, writer, tasks, openTasks)
            .HandleAsync(new ReissueSharedCompanyDocumentAcknowledgementRequest(companyId, doc.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(tasks.Created);
        Assert.Single(writer.Written, n => n.SourceEntityId == existingTaskId);
    }

    [Fact]
    public async Task Reissue_Then_Reminder_Job_Run_Does_Not_Duplicate_The_Task_For_The_Same_Outstanding_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId);

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();
        var openTasks = new FakeOpenTaskBySourceEntityReader();

        await BuildHandler(db, audience, writer, tasks, openTasks)
            .HandleAsync(new ReissueSharedCompanyDocumentAcknowledgementRequest(companyId, doc.Id), Guid.NewGuid(), CancellationToken.None);

        var reissueTask = tasks.Created.Single();
        // The real DB-backed reader would now see that task; wire it into the fake.
        openTasks.AddOpenTaskForAssignee(doc.Id, employeeId, TaskActionType.Acknowledge, reissueTask.Id);

        await BuildJob(db, audience, writer, tasks, openTasks).ExecuteAsync();

        Assert.Single(tasks.Created); // no duplicate task from the job
    }

    [Fact]
    public async Task Reissue_Is_Idempotent_Across_Repeated_Calls_On_Task_Creation_Once_The_Task_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId);

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();
        var openTasks = new FakeOpenTaskBySourceEntityReader();
        var handler = BuildHandler(db, audience, writer, tasks, openTasks);
        var request = new ReissueSharedCompanyDocumentAcknowledgementRequest(companyId, doc.Id);

        await handler.HandleAsync(request, Guid.NewGuid(), CancellationToken.None);
        openTasks.AddOpenTaskForAssignee(doc.Id, employeeId, TaskActionType.Acknowledge, tasks.Created.Single().Id);
        await handler.HandleAsync(request, Guid.NewGuid(), CancellationToken.None);

        // Task not recreated; but the reissue is an explicit "nudge now" so a fresh notification each call is expected.
        Assert.Single(tasks.Created);
        Assert.Equal(2, writer.Written.Count);
    }

    [Fact]
    public async Task Reissue_On_A_Document_From_Another_Company_Returns_NotFound_And_Writes_Nothing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId);

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [Guid.NewGuid()] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();

        var result = await BuildHandler(db, audience, writer, tasks, new FakeOpenTaskBySourceEntityReader())
            .HandleAsync(new ReissueSharedCompanyDocumentAcknowledgementRequest(Guid.NewGuid(), doc.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(writer.Written);
        Assert.Empty(tasks.Created);
    }

    [Fact]
    public async Task Reissue_With_No_Eligible_Audience_Succeeds_With_Zero_Count_And_No_Side_Effects()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var doc = await SeedPublishedDocAsync(db, companyId);

        var audience = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [] };
        var writer = new FakeNotificationWriter();
        var tasks = new FakeTaskCreator();

        var result = await BuildHandler(db, audience, writer, tasks, new FakeOpenTaskBySourceEntityReader())
            .HandleAsync(new ReissueSharedCompanyDocumentAcknowledgementRequest(companyId, doc.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.EmployeesNotifiedCount);
        Assert.Empty(writer.Written);
        Assert.Empty(tasks.Created);
    }
}
