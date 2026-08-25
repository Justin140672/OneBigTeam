using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// DOC-03. The job's own logic (discovering distinct company ids and delegating to
/// ProcessDocumentExpiryNotificationsHandler per company inside a try/catch) is intentionally
/// simple — per-company failure isolation is a straightforward try/catch around a concrete,
/// non-mockable handler class, so rather than force an awkward substitute-handler mock here, that
/// isolation behaviour is exercised end-to-end instead by
/// DocumentExpiryReminderStagesTests/DocumentExpiryTasksEndToEndTests in HR.Integration.Tests,
/// which drive the real handler against real per-company data. These tests focus on what the job
/// itself is responsible for: discovering the correct set of companies to process.
/// </summary>
public class DocumentExpiryReminderJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today        = DateOnly.FromDateTime(FixedUtcNow);

    private static DocumentsDbContext BuildContext(string? dbName = null) =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task SeedDocumentAsync(
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
    }

    private static DocumentExpiryReminderJob BuildJob(
        DocumentsDbContext db,
        FakeCompanyTimeZoneReader? tzReader = null) =>
        new(db,
            new ProcessDocumentExpiryNotificationsHandler(
                db,
                new FakeClock(FixedUtcNow),
                tzReader ?? new FakeCompanyTimeZoneReader(),
                new FakeAuditPublisher(),
                new FakeTaskCreator()),
            new FakeLogger<DocumentExpiryReminderJob>());

    [Fact]
    public async Task ExecuteAsync_Processes_Only_Companies_With_A_Non_Null_ExpiryDate_Document()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyWithExpiry    = Guid.NewGuid();
        var companyWithoutExpiry = Guid.NewGuid();

        await using (var seedDb = BuildContext(dbName))
        {
            await SeedDocumentAsync(seedDb, companyWithExpiry, Guid.NewGuid(), expiryDate: Today.AddDays(10));
            await SeedDocumentAsync(seedDb, companyWithoutExpiry, Guid.NewGuid(), expiryDate: null);
        }

        await using var db = BuildContext(dbName);
        var job = BuildJob(db);

        await job.ExecuteAsync();

        var processedDoc = await db.EmployeeDocuments
            .SingleAsync(ed => ed.CompanyId == companyWithExpiry);
        Assert.NotNull(processedDoc.ExpiryReminder90SentAt);

        var untouchedDoc = await db.EmployeeDocuments
            .SingleAsync(ed => ed.CompanyId == companyWithoutExpiry);
        Assert.Null(untouchedDoc.ExpiryReminder90SentAt);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_No_Documents_Have_An_ExpiryDate()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using (var seedDb = BuildContext(dbName))
        {
            await SeedDocumentAsync(seedDb, Guid.NewGuid(), Guid.NewGuid(), expiryDate: null);
        }

        await using var db = BuildContext(dbName);
        var job = BuildJob(db);

        // Should not throw and should leave the single document untouched.
        await job.ExecuteAsync();

        var doc = await db.EmployeeDocuments.SingleAsync();
        Assert.Null(doc.ExpiryReminder90SentAt);
    }

    [Fact]
    public async Task ExecuteAsync_Processes_Multiple_Companies_Independently()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await using (var seedDb = BuildContext(dbName))
        {
            await SeedDocumentAsync(seedDb, companyA, Guid.NewGuid(), expiryDate: Today.AddDays(10));
            await SeedDocumentAsync(seedDb, companyB, Guid.NewGuid(), expiryDate: Today.AddDays(-1));
        }

        await using var db = BuildContext(dbName);
        var job = BuildJob(db);

        await job.ExecuteAsync();

        var docA = await db.EmployeeDocuments.SingleAsync(ed => ed.CompanyId == companyA);
        var docB = await db.EmployeeDocuments.SingleAsync(ed => ed.CompanyId == companyB);

        Assert.NotNull(docA.ExpiryReminder90SentAt);
        Assert.NotNull(docB.ExpiredNotifiedAt);
    }
}
