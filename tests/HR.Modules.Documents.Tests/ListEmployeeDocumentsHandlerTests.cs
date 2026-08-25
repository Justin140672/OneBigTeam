using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ListEmployeeDocuments;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ListEmployeeDocumentsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 10, 0, 0, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ListEmployeeDocumentsHandler BuildHandler(DocumentsDbContext db) => new(db);

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
        string title         = "Employment Contract",
        DocumentStatus status = DocumentStatus.Active,
        DateTimeOffset? createdAt = null)
    {
        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, title, null,
            documentTypeId, "file.pdf", 1024, "application/pdf",
            $"storage/{Guid.NewGuid():N}/file.pdf",
            null, Guid.NewGuid(), createdAt ?? Now);

        if (status == DocumentStatus.Archived)
            doc.Archive(Now);
        else if (status == DocumentStatus.Expired)
            doc.Expire(Now);

        db.Documents.Add(doc);
        return doc;
    }

    private static EmployeeDocument SeedEmployeeDocument(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        Guid documentId,
        bool acknowledged    = false,
        DateTimeOffset? createdAt = null)
    {
        var ed = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, documentId, Guid.NewGuid(),
            createdAt ?? Now);
        if (acknowledged)
            ed.Acknowledge(Now);
        db.EmployeeDocuments.Add(ed);
        return ed;
    }

    [Fact]
    public async Task HandleAsync_Returns_All_Documents_For_Employee()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var doc1           = SeedDocument(db, companyId, employeeId, dt.Id, "Contract");
        var doc2           = SeedDocument(db, companyId, employeeId, dt.Id, "Handbook");
        SeedEmployeeDocument(db, companyId, employeeId, doc1.Id);
        SeedEmployeeDocument(db, companyId, employeeId, doc2.Id);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new ListEmployeeDocumentsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_Employee_Has_No_Documents()
    {
        await using var db = BuildContext();

        var result = await BuildHandler(db).HandleAsync(
            new ListEmployeeDocumentsRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Documents_For_Other_Employees()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeA      = Guid.NewGuid();
        var employeeB      = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var docA           = SeedDocument(db, companyId, employeeA, dt.Id);
        var docB           = SeedDocument(db, companyId, employeeB, dt.Id);
        SeedEmployeeDocument(db, companyId, employeeA, docA.Id);
        SeedEmployeeDocument(db, companyId, employeeB, docB.Id);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new ListEmployeeDocumentsRequest { CompanyId = companyId, EmployeeId = employeeA },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
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
        SeedEmployeeDocument(db, companyA, employeeId, docA.Id);
        SeedEmployeeDocument(db, companyB, employeeId, docB.Id);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new ListEmployeeDocumentsRequest { CompanyId = companyA, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var active         = SeedDocument(db, companyId, employeeId, dt.Id, status: DocumentStatus.Active);
        var archived       = SeedDocument(db, companyId, employeeId, dt.Id, status: DocumentStatus.Archived);
        SeedEmployeeDocument(db, companyId, employeeId, active.Id);
        SeedEmployeeDocument(db, companyId, employeeId, archived.Id);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new ListEmployeeDocumentsRequest
            {
                CompanyId  = companyId,
                EmployeeId = employeeId,
                Status     = DocumentStatus.Active,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(DocumentStatus.Active, result.Value.Items[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_All_Statuses_When_Filter_Is_Null()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        SeedEmployeeDocument(db, companyId, employeeId,
            SeedDocument(db, companyId, employeeId, dt.Id, status: DocumentStatus.Active).Id);
        SeedEmployeeDocument(db, companyId, employeeId,
            SeedDocument(db, companyId, employeeId, dt.Id, status: DocumentStatus.Archived).Id);
        SeedEmployeeDocument(db, companyId, employeeId,
            SeedDocument(db, companyId, employeeId, dt.Id, status: DocumentStatus.Expired).Id);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new ListEmployeeDocumentsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Includes_DocumentTypeName()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId, "Passport");
        var doc            = SeedDocument(db, companyId, employeeId, dt.Id);
        SeedEmployeeDocument(db, companyId, employeeId, doc.Id);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new ListEmployeeDocumentsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Passport", result.Value!.Items[0].DocumentTypeName);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_CreatedAt_Descending()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var older          = SeedDocument(db, companyId, employeeId, dt.Id, "Older",
                                createdAt: Now.AddDays(-2));
        var newer          = SeedDocument(db, companyId, employeeId, dt.Id, "Newer",
                                createdAt: Now);
        SeedEmployeeDocument(db, companyId, employeeId, older.Id, createdAt: Now.AddDays(-2));
        SeedEmployeeDocument(db, companyId, employeeId, newer.Id, createdAt: Now);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new ListEmployeeDocumentsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Newer", result.Value!.Items[0].Title);
        Assert.Equal("Older", result.Value.Items[1].Title);
    }

    [Fact]
    public async Task HandleAsync_Includes_AcknowledgedAt_When_Acknowledged()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var doc            = SeedDocument(db, companyId, employeeId, dt.Id);
        SeedEmployeeDocument(db, companyId, employeeId, doc.Id, acknowledged: true);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new ListEmployeeDocumentsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Items[0].IsAcknowledged);
    }

    // DOC-04: archived (soft-deleted) employee documents must behave as if they don't exist
    // through the normal list endpoint.
    [Fact]
    public async Task HandleAsync_Excludes_Archived_EmployeeDocuments()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var dt             = SeedDocumentType(db, companyId);
        var visibleDoc     = SeedDocument(db, companyId, employeeId, dt.Id, "Visible");
        var archivedDoc    = SeedDocument(db, companyId, employeeId, dt.Id, "Archived");
        SeedEmployeeDocument(db, companyId, employeeId, visibleDoc.Id);
        var archivedEmpDoc = SeedEmployeeDocument(db, companyId, employeeId, archivedDoc.Id);
        archivedEmpDoc.Archive(Guid.NewGuid(), null, Now);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).HandleAsync(
            new ListEmployeeDocumentsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Visible", item.Title);
    }
}
