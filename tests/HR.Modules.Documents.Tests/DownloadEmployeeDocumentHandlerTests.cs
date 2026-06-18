using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.DownloadEmployeeDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class DownloadEmployeeDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 10, 0, 0, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static DownloadEmployeeDocumentHandler BuildHandler(
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
    public async Task HandleAsync_Returns_DownloadUrl_Derived_From_StorageKey()
    {
        await using var db = BuildContext();
        var storage        = new FakeDocumentStorageService();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (doc, empDoc)  = await Seed(db, companyId, employeeId);
        var handler        = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new DownloadEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = empDoc.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(doc.StorageKey, result.Value!.ToString());
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_EmployeeDocumentId_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new DownloadEmployeeDocumentRequest
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
            new DownloadEmployeeDocumentRequest
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
            new DownloadEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = Guid.NewGuid(),
                EmployeeDocumentId = empDoc.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
