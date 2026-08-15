using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetExpiringDocuments;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetExpiringDocumentsHandlerTests
{
    // Today is 2026-06-18; threshold is 2026-07-18
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly  Today      = DateOnly.FromDateTime(FixedUtcNow);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static GetExpiringDocumentsHandler BuildHandler(
        DocumentsDbContext db,
        DateTime? fixedUtcNow = null,
        FakeCompanyTimeZoneReader? companyTimeZoneReader = null) =>
        new(db, new FakeClock(fixedUtcNow ?? FixedUtcNow), companyTimeZoneReader ?? new FakeCompanyTimeZoneReader());

    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static DocumentType SeedDocumentType(DocumentsDbContext db, Guid companyId, string name = "Contract")
    {
        var dt = DocumentType.Create(Guid.NewGuid(), companyId, name, null, Now);
        db.DocumentTypes.Add(dt);
        return dt;
    }

    private static Document SeedDocument(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        Guid documentTypeId,
        string title = "Employment Contract")
    {
        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, title, null,
            documentTypeId, "file.pdf", 1024, "application/pdf",
            $"storage/{Guid.NewGuid():N}/file.pdf",
            null, Guid.NewGuid(), Now);
        db.Documents.Add(doc);
        return doc;
    }

    private static EmployeeDocument SeedEmployeeDocument(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        Guid documentId,
        DateOnly? expiryDate = null)
    {
        var ed = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, documentId, Guid.NewGuid(), Now,
            expiryDate: expiryDate);
        db.EmployeeDocuments.Add(ed);
        return ed;
    }

    [Fact]
    public async Task HandleAsync_Returns_ExpiringSoon_Documents()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var doc            = SeedDocument(db, companyId, employeeId, dt.Id);
        SeedEmployeeDocument(db, companyId, employeeId, doc.Id, expiryDate: Today.AddDays(15));
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetExpiringDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(DocumentExpiryStatus.ExpiringSoon, result.Value.Items[0].ExpiryStatus);
    }

    [Fact]
    public async Task HandleAsync_Returns_Expired_Documents()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var doc            = SeedDocument(db, companyId, employeeId, dt.Id);
        SeedEmployeeDocument(db, companyId, employeeId, doc.Id, expiryDate: Today.AddDays(-1));
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetExpiringDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(DocumentExpiryStatus.Expired, result.Value.Items[0].ExpiryStatus);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Documents_Expiring_After_Threshold()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var doc            = SeedDocument(db, companyId, employeeId, dt.Id);
        SeedEmployeeDocument(db, companyId, employeeId, doc.Id, expiryDate: Today.AddDays(31));
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetExpiringDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Documents_Without_ExpiryDate()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var doc            = SeedDocument(db, companyId, employeeId, dt.Id);
        SeedEmployeeDocument(db, companyId, employeeId, doc.Id, expiryDate: null);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetExpiringDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Documents_For_Other_Companies()
    {
        await using var db = BuildContext();
        var companyA       = Guid.NewGuid();
        var companyB       = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dtA            = SeedDocumentType(db, companyA);
        var dtB            = SeedDocumentType(db, companyB);
        var docA           = SeedDocument(db, companyA, employeeId, dtA.Id);
        var docB           = SeedDocument(db, companyB, employeeId, dtB.Id);
        SeedEmployeeDocument(db, companyA, employeeId, docA.Id, expiryDate: Today.AddDays(5));
        SeedEmployeeDocument(db, companyB, employeeId, docB.Id, expiryDate: Today.AddDays(5));
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetExpiringDocumentsRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Expiring_Documents()
    {
        await using var db = BuildContext();

        var result = await BuildHandler(db).HandleAsync(
            new GetExpiringDocumentsRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_ExpiryDate_Ascending()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var sooner         = SeedDocument(db, companyId, employeeId, dt.Id, "Sooner");
        var later          = SeedDocument(db, companyId, employeeId, dt.Id, "Later");
        SeedEmployeeDocument(db, companyId, employeeId, sooner.Id, expiryDate: Today.AddDays(5));
        SeedEmployeeDocument(db, companyId, employeeId, later.Id,  expiryDate: Today.AddDays(20));
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetExpiringDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Sooner", result.Value!.Items[0].Title);
        Assert.Equal("Later",  result.Value.Items[1].Title);
    }

    [Fact]
    public async Task HandleAsync_Includes_DocumentTypeName()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId, "Passport");
        var doc            = SeedDocument(db, companyId, employeeId, dt.Id);
        SeedEmployeeDocument(db, companyId, employeeId, doc.Id, expiryDate: Today.AddDays(10));
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetExpiringDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Passport", result.Value!.Items[0].DocumentTypeName);
    }

    [Fact]
    public async Task HandleAsync_ExpiryStatus_Is_ExpiringSoon_When_Expiry_Is_Exactly_At_Threshold()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var doc            = SeedDocument(db, companyId, employeeId, dt.Id);
        SeedEmployeeDocument(db, companyId, employeeId, doc.Id, expiryDate: Today.AddDays(30));
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetExpiringDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(DocumentExpiryStatus.ExpiringSoon, result.Value.Items[0].ExpiryStatus);
    }

    [Fact]
    public async Task HandleAsync_ExpiryStatus_Is_ExpiringSoon_When_Expiry_Is_Today()
    {
        // ExpiryDate == today is not "< today" so it must not be Expired; it must be ExpiringSoon.
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var doc            = SeedDocument(db, companyId, employeeId, dt.Id);
        SeedEmployeeDocument(db, companyId, employeeId, doc.Id, expiryDate: Today);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetExpiringDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(DocumentExpiryStatus.ExpiringSoon, result.Value.Items[0].ExpiryStatus);
    }

    [Fact]
    public async Task HandleAsync_Returns_Documents_Across_Multiple_Employees()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeA      = Guid.NewGuid();
        var employeeB      = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var docA           = SeedDocument(db, companyId, employeeA, dt.Id);
        var docB           = SeedDocument(db, companyId, employeeB, dt.Id);
        SeedEmployeeDocument(db, companyId, employeeA, docA.Id, expiryDate: Today.AddDays(5));
        SeedEmployeeDocument(db, companyId, employeeB, docB.Id, expiryDate: Today.AddDays(10));
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetExpiringDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Contains(result.Value.Items, i => i.EmployeeId == employeeA);
        Assert.Contains(result.Value.Items, i => i.EmployeeId == employeeB);
    }

    [Fact]
    public async Task HandleAsync_Uses_Company_Local_Day_Not_UTC_Day_For_Expired_Vs_ExpiringSoon()
    {
        // At 2026-06-17T23:30:00Z the UTC day is still Jun 17, so an expiry of Jun 17 would still
        // read as "today" (ExpiringSoon) under UTC. But in a fixed UTC+12 zone (no DST) the local
        // day is already Jun 18 — the same document should read as Expired once the company's
        // timezone is applied.
        var fixedUtcNow = new DateTime(2026, 6, 17, 23, 30, 0, DateTimeKind.Utc);

        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dt         = SeedDocumentType(db, companyId);
        var doc        = SeedDocument(db, companyId, employeeId, dt.Id);
        SeedEmployeeDocument(db, companyId, employeeId, doc.Id, expiryDate: new DateOnly(2026, 6, 17));
        await db.SaveChangesAsync();

        var result = await BuildHandler(
            db,
            fixedUtcNow: fixedUtcNow,
            companyTimeZoneReader: new FakeCompanyTimeZoneReader("Etc/GMT-12")).HandleAsync(
            new GetExpiringDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(DocumentExpiryStatus.Expired, result.Value.Items[0].ExpiryStatus);
    }
}
