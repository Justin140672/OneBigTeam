using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.PublishSharedCompanyDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class PublishSharedCompanyDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Publishes_Draft_Document_Successfully()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var publishedBy = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            publishedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Published", result.Value!.Status);
        Assert.Equal(publishedBy, result.Value.PublishedBy);
        Assert.Equal(FixedUtcNow, result.Value.PublishedAt);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(SharedCompanyDocumentStatus.Published, stored.Status);
        Assert.Equal(publishedBy, stored.PublishedBy);
        Assert.Equal(FixedUtcNow, stored.PublishedAt);
        Assert.Equal(publishedBy, stored.UpdatedBy);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_With_No_Audience_Rules_Set()
    {
        // "All Employees" (zero rules) is a deliberate, valid audience — publishing must never
        // be blocked just because no specific department/location/position/employee was picked.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Document()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Belongs_To_Another_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Document_Already_Published()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Document_Archived()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        doc.Publish(Guid.NewGuid(), Now);
        doc.Archive(Guid.NewGuid(), "Superseded", Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Category_Is_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var storedCategory = await db.CompanyDocumentCategories.SingleAsync();
        storedCategory.Deactivate(Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(SharedCompanyDocumentStatus.Draft, stored.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_ReviewDate_Before_EffectiveDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            effectiveDate: new DateOnly(2026, 9, 1), reviewDate: new DateOnly(2026, 1, 1), SharedCompanyDocumentReviewFrequency.None, null,
            requiresAcknowledgement: false, acknowledgementDueDate: null, acknowledgementStatement: null,
            createdBy: Guid.NewGuid(), now: Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Acknowledgement_Required_But_No_DueDate_Set()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, requiresAcknowledgement: true, acknowledgementDueDate: null, acknowledgementStatement: null,
            createdBy: Guid.NewGuid(), now: Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(SharedCompanyDocumentStatus.Draft, stored.Status);
    }

    [Fact]
    public async Task HandleAsync_Publishes_When_Acknowledgement_Required_And_DueDate_Set_But_No_Statement()
    {
        // The statement is explicitly optional — a missing statement must never block publishing,
        // only a missing due date does.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1), acknowledgementStatement: null,
            createdBy: Guid.NewGuid(), now: Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Creates_Acknowledgement_Tasks_For_Eligible_Employees_When_Required()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var publishedBy = Guid.NewGuid();
        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Remote Working Policy", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1), acknowledgementStatement: null,
            createdBy: Guid.NewGuid(), now: Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [emp1, emp2] };
        var taskCreator = new FakeTaskCreator();

        var result = await Handler(db, audienceReader, taskCreator: taskCreator).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            publishedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.AcknowledgementTasksCreated);
        Assert.Equal(2, taskCreator.Created.Count);
        Assert.All(taskCreator.Created, t =>
        {
            Assert.Equal("Acknowledge: Remote Working Policy (v1)", t.Title);
            Assert.Equal("Please read and acknowledge 'Remote Working Policy'.", t.Description);
            Assert.Equal(TaskActionType.Acknowledge, t.ActionType);
            Assert.Equal(TaskSource.Document, t.Source);
            Assert.Equal(doc.Id, t.SourceEntityId);
            Assert.Equal(new DateOnly(2027, 1, 1), t.DueDate);
            Assert.Equal(publishedBy, t.CreatedBy);
        });
        Assert.Contains(taskCreator.Created, t => t.AssignedEmployeeId == emp1);
        Assert.Contains(taskCreator.Created, t => t.AssignedEmployeeId == emp2);
    }

    [Fact]
    public async Task HandleAsync_Writes_Acknowledgement_Reminder_Notifications_For_Eligible_Employees_When_Required()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var publishedBy = Guid.NewGuid();
        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Remote Working Policy", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1), acknowledgementStatement: null,
            createdBy: Guid.NewGuid(), now: Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [emp1, emp2] };
        var notificationWriter = new FakeNotificationWriter();

        var result = await Handler(db, audienceReader, notificationWriter: notificationWriter).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            publishedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, notificationWriter.Written.Count);
        Assert.All(notificationWriter.Written, n =>
        {
            Assert.Equal(NotificationType.SharedCompanyDocumentAcknowledgementReminder, n.Type);
            Assert.Equal(doc.Id, n.SourceEntityId);
            Assert.Equal(companyId, n.CompanyId);
        });
        Assert.Contains(notificationWriter.Written, n => n.EmployeeId == emp1);
        Assert.Contains(notificationWriter.Written, n => n.EmployeeId == emp2);
    }

    [Fact]
    public async Task HandleAsync_Skips_Creating_A_Task_For_An_Employee_Who_Already_Acknowledged_This_Version()
    {
        // Guards against a duplicate reminder: if this exact version was already acknowledged
        // (e.g. between an earlier publish and a metadata-only republish), the employee has
        // already complied and doesn't need a task nagging them to do it again.
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var alreadyAcknowledged = Guid.NewGuid();
        var stillPending        = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Remote Working Policy", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1), acknowledgementStatement: null,
            createdBy: Guid.NewGuid(), now: Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAcknowledgements.Add(SharedCompanyDocumentAcknowledgement.Create(
            Guid.NewGuid(), companyId, doc.Id, alreadyAcknowledged, doc.VersionNumber, "Statement", null, Now));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [alreadyAcknowledged, stillPending] };
        var taskCreator = new FakeTaskCreator();

        var result = await Handler(db, audienceReader, taskCreator: taskCreator).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.AcknowledgementTasksCreated);
        Assert.Single(taskCreator.Created);
        Assert.Equal(stillPending, taskCreator.Created[0].AssignedEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Create_Tasks_When_Acknowledgement_Not_Required()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [Guid.NewGuid()] };
        var taskCreator = new FakeTaskCreator();

        var result = await Handler(db, audienceReader, taskCreator: taskCreator).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.AcknowledgementTasksCreated);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event()
    {
        await using var db = BuildContext();
        var companyId   = Guid.NewGuid();
        var category    = await SeedCategory(db, companyId);
        var publishedBy = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        await Handler(db, auditPublisher: audit).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            publishedBy, CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("shared_company_document.published", evt.EventType);
        Assert.Equal("SharedCompanyDocument",              evt.EntityType);
        Assert.Equal(doc.Id,                                evt.EntityId);
        Assert.Equal(publishedBy,                           evt.ActorUserId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_On_Failure()
    {
        await using var db = BuildContext();

        var audit = new FakeAuditPublisher();
        await Handler(db, auditPublisher: audit).HandleAsync(
            new PublishSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
            Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    private static PublishSharedCompanyDocumentHandler Handler(
        DocumentsDbContext db,
        FakeEmployeeAudienceReader? audienceReader = null,
        FakeAuditPublisher? auditPublisher = null,
        FakeTaskCreator? taskCreator = null,
        FakeNotificationWriter? notificationWriter = null)
    {
        var reader = audienceReader ?? new FakeEmployeeAudienceReader();
        return new PublishSharedCompanyDocumentHandler(
            db,
            new SharedCompanyDocumentAudienceMatcher(db, reader),
            taskCreator ?? new FakeTaskCreator(),
            notificationWriter ?? new FakeNotificationWriter(),
            auditPublisher ?? new FakeAuditPublisher(),
            new FakeClock(FixedUtcNow));
    }

    private static SharedCompanyDocument CreateDoc(Guid companyId, Guid categoryId, Guid createdBy) =>
        SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, categoryId, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, false, null, null, createdBy, Now);

    private static async Task<CompanyDocumentCategory> SeedCategory(
        DocumentsDbContext db, Guid companyId, string name = "Policy")
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, name, Now);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
