using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetArchivedEmployeeDocuments;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetArchivedEmployeeDocumentsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 10, 0, 0, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static GetArchivedEmployeeDocumentsHandler BuildHandler(DocumentsDbContext db) => new(db);

    private static async Task<(DocumentType docType, Document doc, EmployeeDocument empDoc)> Seed(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        string title = "Employment Contract",
        string docTypeName = "Contract")
    {
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, docTypeName, null, Now);
        db.DocumentTypes.Add(docType);

        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, title, null,
            docType.Id, "file.pdf", 1024, "application/pdf",
            $"storage/{Guid.NewGuid():N}/file.pdf",
            null, Guid.NewGuid(), Now);
        db.Documents.Add(doc);

        var empDoc = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), Now);
        db.EmployeeDocuments.Add(empDoc);

        await db.SaveChangesAsync();
        return (docType, doc, empDoc);
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Archived_Documents()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (_, _, archived)   = await Seed(db, companyId, employeeId, "Archived Doc");
        var (_, _, notArchived) = await Seed(db, companyId, employeeId, "Active Doc");
        archived.Archive(Guid.NewGuid(), "Superseded", Now.AddDays(1));
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetArchivedEmployeeDocumentsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(archived.Id, item.EmployeeDocumentId);
        Assert.Equal("Archived Doc", item.Title);
        Assert.DoesNotContain(result.Value.Items, i => i.EmployeeDocumentId == notArchived.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Archived_Documents_Exist()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        await Seed(db, companyId, employeeId);

        var result = await BuildHandler(db).HandleAsync(
            new GetArchivedEmployeeDocumentsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Archived_Documents_For_Other_Employees_Or_Companies()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var otherEmployee  = Guid.NewGuid();
        var otherCompany   = Guid.NewGuid();

        var (_, _, mine)   = await Seed(db, companyId, employeeId);
        var (_, _, other1) = await Seed(db, companyId, otherEmployee);
        var (_, _, other2) = await Seed(db, otherCompany, employeeId);
        mine.Archive(Guid.NewGuid(), null, Now);
        other1.Archive(Guid.NewGuid(), null, Now);
        other2.Archive(Guid.NewGuid(), null, Now);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetArchivedEmployeeDocumentsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(mine.Id, item.EmployeeDocumentId);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_ArchivedAt_Descending()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (_, _, older)  = await Seed(db, companyId, employeeId, "Older Archive");
        var (_, _, newer)  = await Seed(db, companyId, employeeId, "Newer Archive");
        older.Archive(Guid.NewGuid(), null, Now.AddDays(-5));
        newer.Archive(Guid.NewGuid(), null, Now);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetArchivedEmployeeDocumentsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Newer Archive", result.Value!.Items[0].Title);
        Assert.Equal("Older Archive", result.Value.Items[1].Title);
    }

    [Fact]
    public async Task HandleAsync_Includes_ArchivedByUserId_ArchivedAt_ArchiveReason_And_DocumentTypeName()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var archivedBy     = Guid.NewGuid();
        var archivedAt     = Now.AddDays(2);
        var (_, _, empDoc) = await Seed(db, companyId, employeeId, "Passport Scan", "Passport");
        empDoc.Archive(archivedBy, "Employee left the company", archivedAt);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new GetArchivedEmployeeDocumentsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(archivedBy, item.ArchivedByUserId);
        Assert.Equal(archivedAt, item.ArchivedAt);
        Assert.Equal("Employee left the company", item.ArchiveReason);
        Assert.Equal("Passport", item.DocumentTypeName);
        Assert.Equal("Passport Scan", item.Title);
    }
}
