using HR.Modules.Tasks.Contracts;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ProcessDocumentExpiryNotificationsHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today        = DateOnly.FromDateTime(FixedUtcNow);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ProcessDocumentExpiryNotificationsHandler BuildHandler(
        DocumentsDbContext db,
        FakeAuditPublisher? audit        = null,
        FakeTaskCreator?    taskCreator  = null,
        DateTime?           fixedUtcNow  = null,
        FakeCompanyTimeZoneReader? companyTimeZoneReader = null) =>
        new(db,
            new FakeClock(fixedUtcNow ?? FixedUtcNow),
            companyTimeZoneReader ?? new FakeCompanyTimeZoneReader(),
            audit       ?? new FakeAuditPublisher(),
            taskCreator ?? new FakeTaskCreator());

    private static async Task<EmployeeDocument> SeedDocumentAsync(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        DateOnly? expiryDate,
        string title = "Employment Contract",
        string typeName = "Contract")
    {
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, typeName, null, DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(docType);

        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, title, null,
            docType.Id, "contract.pdf", 1024, "application/pdf",
            $"{companyId}/{employeeId}/contract.pdf",
            null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.Documents.Add(doc);

        var empDoc = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), DateTimeOffset.UtcNow,
            expiryDate: expiryDate);
        db.EmployeeDocuments.Add(empDoc);

        await db.SaveChangesAsync();
        return empDoc;
    }

    // ── No-op cases ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Returns_Zero_Counts_When_No_Documents_Have_ExpiryDate()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: null);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.ExpiringSoonCount);
        Assert.Equal(0, result.ExpiredCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_Zero_Counts_When_Document_Expires_Beyond_Widest_Threshold()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(91));
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.ExpiringSoonCount);
        Assert.Equal(0, result.ExpiredCount);
    }

    // ── 90-day stage ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Fires_Only_NinetyDay_Stage_When_Document_Is_Exactly_90_Days_Out()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var audit          = new FakeAuditPublisher();
        var tasks          = new FakeTaskCreator();
        var empDoc         = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(90));
        var handler        = BuildHandler(db, audit, tasks);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(1, result.Reminder90Count);
        Assert.Equal(0, result.Reminder30Count);
        Assert.Equal(0, result.Reminder7Count);
        Assert.Equal(1, result.ExpiringSoonCount);
        Assert.Equal(0, result.ExpiredCount);

        var updated = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        Assert.NotNull(updated!.ExpiryReminder90SentAt);
        Assert.Null(updated.ExpiryReminder30SentAt);
        Assert.Null(updated.ExpiryReminder7SentAt);
        // 90-day stage is not the "legacy" stage, so ExpiringSoonNotifiedAt must remain untouched.
        Assert.Null(updated.ExpiringSoonNotifiedAt);

        var task = Assert.Single(tasks.Created);
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Single(audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Fire_NinetyDay_Stage_One_Day_Before_Boundary()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(91));
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.Reminder90Count);
        Assert.Equal(0, result.ExpiringSoonCount);
    }

    // ── 30-day stage boundary ────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Sets_ThirtyDay_SentAt_Exactly_On_Boundary()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        // Seed the document already past the 90-day threshold on a prior run so this run only
        // exercises the 30-day boundary in isolation.
        var empDoc = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(30));
        var entity = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        entity!.MarkExpiryReminderSent(ExpiryReminderStage.NinetyDays, FixedUtcNow);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(1, result.Reminder30Count);
        Assert.Equal(0, result.Reminder90Count);
        Assert.Equal(0, result.Reminder7Count);

        var updated = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        Assert.NotNull(updated!.ExpiryReminder30SentAt);
        Assert.NotNull(updated.ExpiringSoonNotifiedAt); // legacy flag kept in sync by the 30-day stage
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Set_ThirtyDay_SentAt_One_Day_Before_Boundary()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var empDoc = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(31));
        var entity = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        entity!.MarkExpiryReminderSent(ExpiryReminderStage.NinetyDays, FixedUtcNow);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.Reminder30Count);

        var updated = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        Assert.Null(updated!.ExpiryReminder30SentAt);
        Assert.Null(updated.ExpiringSoonNotifiedAt);
    }

    // ── 7-day stage boundary ─────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Sets_SevenDay_SentAt_Exactly_On_Boundary()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var empDoc = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(7));
        var entity = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        entity!.MarkExpiryReminderSent(ExpiryReminderStage.NinetyDays, FixedUtcNow);
        entity.MarkExpiryReminderSent(ExpiryReminderStage.ThirtyDays, FixedUtcNow);
        await db.SaveChangesAsync();

        var tasks   = new FakeTaskCreator();
        var handler = BuildHandler(db, taskCreator: tasks);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(1, result.Reminder7Count);
        Assert.Equal(0, result.Reminder90Count);
        Assert.Equal(0, result.Reminder30Count);

        var updated = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        Assert.NotNull(updated!.ExpiryReminder7SentAt);

        var task = Assert.Single(tasks.Created);
        Assert.Equal(TaskPriority.Critical, task.Priority);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Set_SevenDay_SentAt_One_Day_Before_Boundary()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var empDoc = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(8));
        var entity = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        entity!.MarkExpiryReminderSent(ExpiryReminderStage.NinetyDays, FixedUtcNow);
        entity.MarkExpiryReminderSent(ExpiryReminderStage.ThirtyDays, FixedUtcNow);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.Reminder7Count);

        var updated = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        Assert.Null(updated!.ExpiryReminder7SentAt);
    }

    // ── Catch-up semantics: multiple stages firing in a single run ─────────────────

    [Fact]
    public async Task HandleAsync_Fires_NinetyAnd_ThirtyDay_Stages_Together_On_First_Evaluation()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var tasks          = new FakeTaskCreator();
        var audit          = new FakeAuditPublisher();
        var empDoc         = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(10));
        var handler        = BuildHandler(db, audit, tasks);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(1, result.Reminder90Count);
        Assert.Equal(1, result.Reminder30Count);
        Assert.Equal(0, result.Reminder7Count);
        Assert.Equal(2, result.ExpiringSoonCount);
        Assert.Equal(2, tasks.Created.Count);
        Assert.Equal(2, audit.Published.Count);

        var updated = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        Assert.NotNull(updated!.ExpiryReminder90SentAt);
        Assert.NotNull(updated.ExpiryReminder30SentAt);
        Assert.Null(updated.ExpiryReminder7SentAt);
    }

    [Fact]
    public async Task HandleAsync_Fires_All_Three_Stages_Together_When_Document_Is_Inside_Seven_Day_Window_On_First_Run()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var tasks          = new FakeTaskCreator();
        var empDoc         = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(3));
        var handler        = BuildHandler(db, taskCreator: tasks);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(1, result.Reminder90Count);
        Assert.Equal(1, result.Reminder30Count);
        Assert.Equal(1, result.Reminder7Count);
        Assert.Equal(3, result.ExpiringSoonCount);
        Assert.Equal(3, tasks.Created.Count);
        Assert.Equal(1, tasks.Created.Count(t => t.Priority == TaskPriority.Critical));
        Assert.Equal(2, tasks.Created.Count(t => t.Priority == TaskPriority.High));

        var updated = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        Assert.NotNull(updated!.ExpiryReminder90SentAt);
        Assert.NotNull(updated.ExpiryReminder30SentAt);
        Assert.NotNull(updated.ExpiryReminder7SentAt);
    }

    // ── Idempotency across repeated runs ────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Second_Run_Returns_Zero_Counts_For_Stages_Already_Sent_And_Preserves_Timestamps()
    {
        var dbName    = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();

        DateTimeOffset? firstRun90;
        DateTimeOffset? firstRun30;

        await using (var db1 = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(dbName).Options))
        {
            var empDoc = await SeedDocumentAsync(db1, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(10));
            await BuildHandler(db1).HandleAsync(
                new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
                CancellationToken.None);

            var updated = await db1.EmployeeDocuments.FindAsync(empDoc.Id);
            firstRun90 = updated!.ExpiryReminder90SentAt;
            firstRun30 = updated.ExpiryReminder30SentAt;
        }

        // Second run happens "later" — a later fixed clock must not overwrite the stored timestamps.
        var laterUtcNow = FixedUtcNow.AddDays(1);

        await using (var db2 = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(dbName).Options))
        {
            var result = await BuildHandler(db2, fixedUtcNow: laterUtcNow).HandleAsync(
                new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
                CancellationToken.None);

            Assert.Equal(0, result.Reminder90Count);
            Assert.Equal(0, result.Reminder30Count);
            Assert.Equal(0, result.Reminder7Count);
            Assert.Equal(0, result.ExpiringSoonCount);

            var empDocId = await db2.EmployeeDocuments.Select(e => e.Id).SingleAsync();
            var updated  = await db2.EmployeeDocuments.FindAsync(empDocId);
            Assert.Equal(firstRun90, updated!.ExpiryReminder90SentAt);
            Assert.Equal(firstRun30, updated.ExpiryReminder30SentAt);
        }
    }

    // ── Expired ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Returns_Expired_Count_And_Publishes_Audit_Event()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var audit          = new FakeAuditPublisher();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(-1));
        var handler        = BuildHandler(db, audit);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.ExpiringSoonCount);
        Assert.Equal(1, result.ExpiredCount);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("document.expired",         evt.EventType);
        Assert.Equal("EmployeeDocument",         evt.EntityType);
        Assert.Equal(companyId,                  evt.CompanyId);
        Assert.Null(evt.ActorUserId);
        Assert.Contains("Employment Contract",   evt.Summary);
    }

    [Fact]
    public async Task HandleAsync_Creates_Critical_Priority_Task_For_Expired()
    {
        await using var db  = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var tasks           = new FakeTaskCreator();
        var empDoc          = await SeedDocumentAsync(db, companyId, employeeId, expiryDate: Today.AddDays(-5));
        var handler         = BuildHandler(db, taskCreator: tasks);

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        var task = Assert.Single(tasks.Created);
        Assert.Equal(TaskPriority.Critical,       task.Priority);
        Assert.Equal(TaskSource.Document,         task.Source);
        Assert.Equal(companyId,                   task.CompanyId);
        Assert.Equal(employeeId,                  task.AssignedEmployeeId);
        Assert.Equal(empDoc.Id,                   task.SourceEntityId);
        Assert.Equal(Today.AddDays(7),            task.DueDate);
        Assert.Contains("Employment Contract",    task.Title);
        Assert.Contains("expired",                task.Title);
    }

    [Fact]
    public async Task HandleAsync_Sets_ExpiredNotifiedAt_After_Processing()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var empDoc         = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(-5));
        var handler        = BuildHandler(db);

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        var updated = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        Assert.NotNull(updated!.ExpiredNotifiedAt);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Repeat_Expired_Notification()
    {
        var dbName    = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var audit     = new FakeAuditPublisher();
        var tasks     = new FakeTaskCreator();

        await using (var db1 = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(dbName).Options))
        {
            await SeedDocumentAsync(db1, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(-5));
            await BuildHandler(db1, audit, tasks).HandleAsync(
                new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
                CancellationToken.None);
        }

        await using (var db2 = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(dbName).Options))
        {
            await BuildHandler(db2, audit, tasks).HandleAsync(
                new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
                CancellationToken.None);
        }

        Assert.Single(audit.Published);
        Assert.Single(tasks.Created);
    }

    // ── Multi-document and isolation ────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Processes_Multiple_Documents_In_One_Pass()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var audit          = new FakeAuditPublisher();
        var tasks          = new FakeTaskCreator();
        // Seed each with earlier stages already sent, so this run isolates a single new stage per doc.
        var docA = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(25));
        var entityA = await db.EmployeeDocuments.FindAsync(docA.Id);
        entityA!.MarkExpiryReminderSent(ExpiryReminderStage.NinetyDays, FixedUtcNow);
        var docB = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(60));
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(-3));
        await db.SaveChangesAsync();
        var handler        = BuildHandler(db, audit, tasks);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(2, result.ExpiringSoonCount); // docA (30-day) + docB (90-day)
        Assert.Equal(1, result.ExpiredCount);
        Assert.Equal(3, audit.Published.Count);
        Assert.Equal(3, tasks.Created.Count);
        Assert.Equal(1, tasks.Created.Count(t => t.Priority == TaskPriority.Critical));
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Process_Documents_From_Other_Company()
    {
        await using var db = BuildContext();
        var companyA       = Guid.NewGuid();
        var companyB       = Guid.NewGuid();
        var audit          = new FakeAuditPublisher();
        var tasks          = new FakeTaskCreator();
        await SeedDocumentAsync(db, companyB, Guid.NewGuid(), expiryDate: Today.AddDays(5));
        var handler        = BuildHandler(db, audit, tasks);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Equal(0, result.ExpiringSoonCount);
        Assert.Equal(0, result.ExpiredCount);
        Assert.Empty(audit.Published);
        Assert.Empty(tasks.Created);
    }

    [Fact]
    public async Task HandleAsync_Task_SourceEntityId_Matches_EmployeeDocumentId()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var tasks          = new FakeTaskCreator();
        var empDoc         = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(7));
        var handler        = BuildHandler(db, taskCreator: tasks);

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.All(tasks.Created, t => Assert.Equal(empDoc.Id, t.SourceEntityId));
    }

    [Fact]
    public async Task HandleAsync_Uses_Company_Local_Day_Not_UTC_Day_To_Classify_Expired_Vs_ExpiringSoon()
    {
        // At 2026-06-17T23:30:00Z the UTC day is still Jun 17. In a fixed UTC+12 zone (no DST) the
        // local day is already Jun 18, so a document expiring Jun 17 must be classified Expired
        // (not ExpiringSoon) once the company's timezone is applied.
        var fixedUtcNow = new DateTime(2026, 6, 17, 23, 30, 0, DateTimeKind.Utc);

        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var audit = new FakeAuditPublisher();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: new DateOnly(2026, 6, 17));
        var handler = BuildHandler(
            db, audit,
            fixedUtcNow: fixedUtcNow,
            companyTimeZoneReader: new FakeCompanyTimeZoneReader("Etc/GMT-12"));

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.ExpiringSoonCount);
        Assert.Equal(1, result.ExpiredCount);
    }

    [Fact]
    public async Task HandleAsync_Requests_TimeZone_For_The_Given_CompanyId()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var requestedFor   = new List<Guid>();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(10));

        var handler = new ProcessDocumentExpiryNotificationsHandler(
            db,
            new FakeClock(FixedUtcNow),
            new RecordingCompanyTimeZoneReader(requestedFor, "UTC"),
            new FakeAuditPublisher(),
            new FakeTaskCreator());

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Contains(companyId, requestedFor);
    }

    private sealed class RecordingCompanyTimeZoneReader(List<Guid> requestedFor, string timeZoneId)
        : HR.Modules.Companies.Contracts.ICompanyTimeZoneReader
    {
        public Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken)
        {
            requestedFor.Add(companyId);
            return Task.FromResult(timeZoneId);
        }
    }
}
