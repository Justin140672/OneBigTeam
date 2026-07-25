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
    public async Task HandleAsync_Returns_Zero_Counts_When_Document_Expires_Beyond_Threshold()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(31));
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.ExpiringSoonCount);
        Assert.Equal(0, result.ExpiredCount);
    }

    // ── ExpiringSoon ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Returns_ExpiringSoon_Count_And_Publishes_Audit_Event()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var audit          = new FakeAuditPublisher();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(15));
        var handler        = BuildHandler(db, audit);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(1, result.ExpiringSoonCount);
        Assert.Equal(0, result.ExpiredCount);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("document.expiring_soon",  evt.EventType);
        Assert.Equal("EmployeeDocument",         evt.EntityType);
        Assert.Equal(companyId,                  evt.CompanyId);
        Assert.Null(evt.ActorUserId);
        Assert.Contains("Employment Contract",   evt.Summary);
    }

    [Fact]
    public async Task HandleAsync_Creates_High_Priority_Task_For_ExpiringSoon()
    {
        await using var db  = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var tasks           = new FakeTaskCreator();
        var empDoc          = await SeedDocumentAsync(db, companyId, employeeId, expiryDate: Today.AddDays(10));
        var handler         = BuildHandler(db, taskCreator: tasks);

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        var task = Assert.Single(tasks.Created);
        Assert.Equal(TaskPriority.High,           task.Priority);
        Assert.Equal(TaskSource.Document,         task.Source);
        Assert.Equal(companyId,                   task.CompanyId);
        Assert.Equal(employeeId,                  task.AssignedEmployeeId);
        Assert.Equal(empDoc.Id,                   task.SourceEntityId);
        Assert.Equal(Today.AddDays(10),           task.DueDate);
        Assert.Contains("Employment Contract",    task.Title);
        Assert.Contains("expiring soon",          task.Title);
        Assert.Contains("10",                     task.Description);
    }

    [Fact]
    public async Task HandleAsync_Includes_Document_Expiring_Today()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var audit          = new FakeAuditPublisher();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today);
        var handler        = BuildHandler(db, audit);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(1, result.ExpiringSoonCount);
        Assert.Equal(0, result.ExpiredCount);
    }

    [Fact]
    public async Task HandleAsync_ExpiringSoon_Event_Has_Correct_DaysUntilExpiry()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var audit          = new FakeAuditPublisher();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(10));
        var handler        = BuildHandler(db, audit);

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        var evt = (DocumentExpiringSoonAuditEvent)audit.Published[0];
        Assert.Equal(10, evt.DaysUntilExpiry);
    }

    [Fact]
    public async Task HandleAsync_Sets_ExpiringSoonNotifiedAt_After_Processing()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var empDoc         = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(10));
        var handler        = BuildHandler(db);

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        var updated = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        Assert.NotNull(updated!.ExpiringSoonNotifiedAt);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Repeat_ExpiringSoon_Notification()
    {
        var dbName    = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var audit     = new FakeAuditPublisher();
        var tasks     = new FakeTaskCreator();

        await using (var db1 = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(dbName).Options))
        {
            await SeedDocumentAsync(db1, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(10));
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
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(10));
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(20));
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(-3));
        var handler        = BuildHandler(db, audit, tasks);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(2, result.ExpiringSoonCount);
        Assert.Equal(1, result.ExpiredCount);
        Assert.Equal(3, audit.Published.Count);
        Assert.Equal(3, tasks.Created.Count);
        Assert.Equal(2, tasks.Created.Count(t => t.Priority == TaskPriority.High));
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
        var empDoc         = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(10));
        var handler        = BuildHandler(db, taskCreator: tasks);

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(empDoc.Id, tasks.Created[0].SourceEntityId);
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
}
