using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ExpiringEmployeeDocumentReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 30);
    private const int Lookahead = 30;

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static EmployeeDocument Seed(
        DocumentsDbContext db,
        Guid companyId,
        string documentTypeName,
        DateOnly? expiryDate,
        Guid? employeeId = null,
        string title = "My Document")
    {
        var emp = employeeId ?? Guid.NewGuid();
        var dt = DocumentType.Create(Guid.NewGuid(), companyId, documentTypeName, null, Now);
        db.DocumentTypes.Add(dt);
        var doc = Document.Create(
            Guid.NewGuid(), companyId, emp, title, null, dt.Id, "file.pdf", 1024, "application/pdf",
            $"storage/{Guid.NewGuid():N}/file.pdf", null, Guid.NewGuid(), Now);
        db.Documents.Add(doc);
        var ed = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, emp, doc.Id, Guid.NewGuid(), Now, expiryDate: expiryDate);
        db.EmployeeDocuments.Add(ed);
        return ed;
    }

    private static async Task<IReadOnlyList<ExpiringEmployeeDocumentItem>> Read(DocumentsDbContext db, Guid companyId) =>
        await new ExpiringEmployeeDocumentReader(db)
            .GetExpiringEmployeeDocumentsAsync(companyId, Today, Lookahead, CancellationToken.None);

    [Theory]
    [InlineData("Work Visa", ComplianceDocumentKind.Immigration)]
    [InlineData("Passport", ComplianceDocumentKind.Immigration)]
    [InlineData("Right to Work", ComplianceDocumentKind.Immigration)]
    [InlineData("First Aid Certificate", ComplianceDocumentKind.Certification)]
    [InlineData("Professional Qualification", ComplianceDocumentKind.Certification)]
    [InlineData("Driving Licence", ComplianceDocumentKind.Certification)]
    [InlineData("Contract", ComplianceDocumentKind.Other)]
    public async Task Classifies_DocumentType_Name_By_Keyword(string typeName, ComplianceDocumentKind expected)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        Seed(db, companyId, typeName, Today.AddDays(10));
        await db.SaveChangesAsync();

        var result = await Read(db, companyId);

        var item = Assert.Single(result);
        Assert.Equal(expected, item.Kind);
    }

    [Fact]
    public async Task Includes_Documents_Expiring_Within_Window_And_Already_Expired_But_Not_Beyond()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        Seed(db, companyId, "Passport", Today.AddDays(-5), title: "Expired");
        Seed(db, companyId, "Passport", Today.AddDays(Lookahead), title: "On boundary");
        Seed(db, companyId, "Passport", Today.AddDays(Lookahead + 1), title: "Beyond window");
        Seed(db, companyId, "Passport", null, title: "No expiry");
        await db.SaveChangesAsync();

        var result = await Read(db, companyId);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.DocumentTitle == "Expired");
        Assert.Contains(result, r => r.DocumentTitle == "On boundary");
    }

    [Fact]
    public async Task Excludes_Archived_Documents()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var ed = Seed(db, companyId, "Passport", Today.AddDays(10));
        ed.Archive(Guid.NewGuid(), "no longer needed", Now);
        await db.SaveChangesAsync();

        Assert.Empty(await Read(db, companyId));
    }

    [Fact]
    public async Task Excludes_Non_Latest_Version_Documents()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var ed = Seed(db, companyId, "Passport", Today.AddDays(10));
        ed.SupersedeAsPreviousVersion(Now);
        await db.SaveChangesAsync();

        Assert.Empty(await Read(db, companyId));
    }

    [Fact]
    public async Task Is_Scoped_By_CompanyId()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        Seed(db, companyA, "Passport", Today.AddDays(10));
        Seed(db, companyB, "Passport", Today.AddDays(10));
        await db.SaveChangesAsync();

        Assert.Single(await Read(db, companyA));
    }

    [Fact]
    public async Task Surfaces_Title_TypeName_And_ExpiryDate_Ordered_By_ExpiryDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        Seed(db, companyId, "Work Visa", Today.AddDays(20), title: "Later");
        Seed(db, companyId, "Passport", Today.AddDays(3), title: "Sooner");
        await db.SaveChangesAsync();

        var result = await Read(db, companyId);

        Assert.Equal("Sooner", result[0].DocumentTitle);
        Assert.Equal(Today.AddDays(3), result[0].ExpiryDate);
        Assert.Equal("Passport", result[0].DocumentTypeName);
    }
}
