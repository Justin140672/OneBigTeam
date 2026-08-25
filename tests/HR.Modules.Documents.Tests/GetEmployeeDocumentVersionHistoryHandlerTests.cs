using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetEmployeeDocumentVersionHistory;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetEmployeeDocumentVersionHistoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 10, 0, 0, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static GetEmployeeDocumentVersionHistoryHandler BuildHandler(DocumentsDbContext db) => new(db);

    private static async Task<EmployeeDocument> SeedVersion(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        DocumentType docType,
        DateTimeOffset createdAt,
        Guid? previousVersionId = null,
        string fileName = "file.pdf")
    {
        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, "Passport", null,
            docType.Id, fileName, 1024, "application/pdf",
            $"storage/{Guid.NewGuid():N}/{fileName}",
            null, Guid.NewGuid(), createdAt);
        db.Documents.Add(doc);

        var empDoc = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), createdAt,
            previousVersionId: previousVersionId);
        db.EmployeeDocuments.Add(empDoc);

        await db.SaveChangesAsync();
        return empDoc;
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Id()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeDocumentVersionHistoryRequest
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
    public async Task HandleAsync_Single_Version_Lineage_Returns_One_Item()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var docType        = DocumentType.Create(Guid.NewGuid(), companyId, "Passport", null, Now);
        db.DocumentTypes.Add(docType);
        await db.SaveChangesAsync();

        var only = await SeedVersion(db, companyId, employeeId, docType, Now);
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeDocumentVersionHistoryRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = only.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Versions);
        Assert.Equal(only.Id, item.EmployeeDocumentId);
        Assert.Null(item.PreviousVersionId);
        Assert.True(item.IsLatestVersion);
    }

    [Fact]
    public async Task HandleAsync_Multi_Version_Lineage_Returns_All_Versions_NewestFirst_Regardless_Of_Anchor()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var docType        = DocumentType.Create(Guid.NewGuid(), companyId, "Passport", null, Now);
        db.DocumentTypes.Add(docType);
        await db.SaveChangesAsync();

        var v1 = await SeedVersion(db, companyId, employeeId, docType, Now, fileName: "v1.pdf");
        v1.SupersedeAsPreviousVersion(Now.AddDays(1));
        await db.SaveChangesAsync();

        var v2 = await SeedVersion(db, companyId, employeeId, docType, Now.AddDays(1), previousVersionId: v1.Id, fileName: "v2.pdf");
        v2.SupersedeAsPreviousVersion(Now.AddDays(2));
        await db.SaveChangesAsync();

        var v3 = await SeedVersion(db, companyId, employeeId, docType, Now.AddDays(2), previousVersionId: v2.Id, fileName: "v3.pdf");

        var handler = BuildHandler(db);

        // Anchor on the OLDEST version's id — must still return the whole chain, newest-first.
        var result = await handler.HandleAsync(
            new GetEmployeeDocumentVersionHistoryRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = v1.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Versions.Count);
        Assert.Equal([v3.Id, v2.Id, v1.Id], result.Value.Versions.Select(v => v.EmployeeDocumentId));
        Assert.True(result.Value.Versions[0].IsLatestVersion);
        Assert.False(result.Value.Versions[1].IsLatestVersion);
        Assert.False(result.Value.Versions[2].IsLatestVersion);

        // Anchor on the MIDDLE version's id — same result.
        var resultFromMiddle = await handler.HandleAsync(
            new GetEmployeeDocumentVersionHistoryRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = v2.Id,
            },
            CancellationToken.None);

        Assert.True(resultFromMiddle.IsSuccess);
        Assert.Equal([v3.Id, v2.Id, v1.Id], resultFromMiddle.Value!.Versions.Select(v => v.EmployeeDocumentId));

        // Anchor on the NEWEST (latest) version's id — same result.
        var resultFromLatest = await handler.HandleAsync(
            new GetEmployeeDocumentVersionHistoryRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = v3.Id,
            },
            CancellationToken.None);

        Assert.True(resultFromLatest.IsSuccess);
        Assert.Equal([v3.Id, v2.Id, v1.Id], resultFromLatest.Value!.Versions.Select(v => v.EmployeeDocumentId));
    }
}
