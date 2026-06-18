using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ProcessDocumentExpiryNotificationsHandlerTests
{
    // Fixed "today" for all tests
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today        = DateOnly.FromDateTime(FixedUtcNow);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ProcessDocumentExpiryNotificationsHandler BuildHandler(
        DocumentsDbContext db,
        FakeAuditPublisher? audit = null) =>
        new(db, new FakeClock(FixedUtcNow), audit ?? new FakeAuditPublisher());

    private static async Task<EmployeeDocument> SeedDocumentAsync(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        DateOnly? expiryDate)
    {
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(docType);

        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, "Employment Contract", null,
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
    public async Task HandleAsync_Returns_ExpiringSoon_Count_And_Publishes_Event()
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
        Assert.Equal("document.expiring_soon", evt.EventType);
        Assert.Equal("EmployeeDocument",       evt.EntityType);
        Assert.Equal(companyId,                evt.CompanyId);
        Assert.Null(evt.ActorUserId);
        Assert.Contains("Employment Contract", evt.Summary);
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
        await using var db   = BuildContext();
        var companyId        = Guid.NewGuid();
        var audit            = new FakeAuditPublisher();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(10));
        var handler          = BuildHandler(db, audit);

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        var evt = (DocumentExpiringSoonAuditEvent)audit.Published[0];
        Assert.Equal(10, evt.DaysUntilExpiry);
    }

    [Fact]
    public async Task HandleAsync_Sets_ExpiringSoonNotifiedAt_After_Processing()
    {
        await using var db   = BuildContext();
        var companyId        = Guid.NewGuid();
        var empDoc           = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(10));
        var handler          = BuildHandler(db);

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        var updated = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        Assert.NotNull(updated!.ExpiringSoonNotifiedAt);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Repeat_ExpiringSoon_Notification()
    {
        await using var db   = BuildContext();
        var companyId        = Guid.NewGuid();
        var audit            = new FakeAuditPublisher();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(10));
        var handler          = BuildHandler(db, audit);

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);
        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Single(audit.Published);
    }

    // ── Expired ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Returns_Expired_Count_And_Publishes_Event()
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
        Assert.Equal("document.expired",       evt.EventType);
        Assert.Equal("EmployeeDocument",       evt.EntityType);
        Assert.Equal(companyId,                evt.CompanyId);
        Assert.Null(evt.ActorUserId);
        Assert.Contains("Employment Contract", evt.Summary);
    }

    [Fact]
    public async Task HandleAsync_Sets_ExpiredNotifiedAt_After_Processing()
    {
        await using var db   = BuildContext();
        var companyId        = Guid.NewGuid();
        var empDoc           = await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(-5));
        var handler          = BuildHandler(db);

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        var updated = await db.EmployeeDocuments.FindAsync(empDoc.Id);
        Assert.NotNull(updated!.ExpiredNotifiedAt);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Repeat_Expired_Notification()
    {
        await using var db   = BuildContext();
        var companyId        = Guid.NewGuid();
        var audit            = new FakeAuditPublisher();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(-5));
        var handler          = BuildHandler(db, audit);

        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);
        await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Single(audit.Published);
    }

    // ── Multi-document and isolation ────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Processes_Multiple_Documents_In_One_Pass()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var audit          = new FakeAuditPublisher();
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(10));
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(20));
        await SeedDocumentAsync(db, companyId, Guid.NewGuid(), expiryDate: Today.AddDays(-3));
        var handler        = BuildHandler(db, audit);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(2, result.ExpiringSoonCount);
        Assert.Equal(1, result.ExpiredCount);
        Assert.Equal(3, audit.Published.Count);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Process_Documents_From_Other_Company()
    {
        await using var db    = BuildContext();
        var companyA          = Guid.NewGuid();
        var companyB          = Guid.NewGuid();
        var audit             = new FakeAuditPublisher();
        await SeedDocumentAsync(db, companyB, Guid.NewGuid(), expiryDate: Today.AddDays(5));
        var handler           = BuildHandler(db, audit);

        var result = await handler.HandleAsync(
            new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Equal(0, result.ExpiringSoonCount);
        Assert.Equal(0, result.ExpiredCount);
        Assert.Empty(audit.Published);
    }
}
