using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.DeleteEmployeeDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class DeleteEmployeeDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 10, 0, 0, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static DeleteEmployeeDocumentHandler BuildHandler(
        DocumentsDbContext db,
        FakeDocumentStorageService? storage = null) =>
        new(db, storage ?? new FakeDocumentStorageService());

    private static async Task<(Document doc, EmployeeDocument empDoc)> Seed(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId)
    {
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, Now);
        db.DocumentTypes.Add(docType);

        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, "Employment Contract", null,
            docType.Id, "contract.pdf", 1024, "application/pdf",
            $"{companyId}/{employeeId}/abc/contract.pdf",
            null, Guid.NewGuid(), Now);
        db.Documents.Add(doc);

        var empDoc = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), Now);
        db.EmployeeDocuments.Add(empDoc);

        await db.SaveChangesAsync();
        return (doc, empDoc);
    }

    [Fact]
    public async Task HandleAsync_Deletes_EmployeeDocument_And_Document()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (_, empDoc)    = await Seed(db, companyId, employeeId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new DeleteEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = empDoc.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.EmployeeDocuments.ToListAsync());
        Assert.Empty(await db.Documents.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Deletes_File_From_Storage_When_Last_Link()
    {
        await using var db = BuildContext();
        var storage        = new FakeDocumentStorageService();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (doc, empDoc)  = await Seed(db, companyId, employeeId);
        var handler        = BuildHandler(db, storage);

        await handler.HandleAsync(
            new DeleteEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = empDoc.Id,
            },
            CancellationToken.None);

        Assert.Single(storage.Deletions);
        Assert.Equal(doc.StorageKey, storage.Deletions[0]);
    }

    [Fact]
    public async Task HandleAsync_Keeps_Document_When_Other_Employees_Are_Linked()
    {
        await using var db  = BuildContext();
        var storage         = new FakeDocumentStorageService();
        var companyId       = Guid.NewGuid();
        var employeeA       = Guid.NewGuid();
        var employeeB       = Guid.NewGuid();
        var (doc, empDocA)  = await Seed(db, companyId, employeeA);

        // Link the same document to a second employee
        var empDocB = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeB, doc.Id, Guid.NewGuid(), Now);
        db.EmployeeDocuments.Add(empDocB);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new DeleteEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeA,
                EmployeeDocumentId = empDocA.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.EmployeeDocuments.Where(ed => ed.Id == empDocA.Id).ToListAsync());
        Assert.Single(await db.Documents.ToListAsync());
        Assert.Empty(storage.Deletions);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_EmployeeDocumentId_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new DeleteEmployeeDocumentRequest
            {
                CompanyId          = Guid.NewGuid(),
                EmployeeId         = Guid.NewGuid(),
                EmployeeDocumentId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_CompanyId_Does_Not_Match()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (_, empDoc)    = await Seed(db, companyId, employeeId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new DeleteEmployeeDocumentRequest
            {
                CompanyId          = Guid.NewGuid(),
                EmployeeId         = employeeId,
                EmployeeDocumentId = empDoc.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_EmployeeId_Does_Not_Match()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (_, empDoc)    = await Seed(db, companyId, employeeId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new DeleteEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = Guid.NewGuid(),
                EmployeeDocumentId = empDoc.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Delete_File_When_NotFound()
    {
        await using var db = BuildContext();
        var storage        = new FakeDocumentStorageService();
        var handler        = BuildHandler(db, storage);

        await handler.HandleAsync(
            new DeleteEmployeeDocumentRequest
            {
                CompanyId          = Guid.NewGuid(),
                EmployeeId         = Guid.NewGuid(),
                EmployeeDocumentId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.Empty(storage.Deletions);
    }
}
