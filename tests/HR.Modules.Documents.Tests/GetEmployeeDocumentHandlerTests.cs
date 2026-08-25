using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetEmployeeDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetEmployeeDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static GetEmployeeDocumentHandler BuildHandler(
        DocumentsDbContext db) =>
        new(db);

    private static async Task<(DocumentType docType, Document doc, EmployeeDocument empDoc)> SeedAll(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        Guid uploadedBy,
        bool acknowledged = false)
    {
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, Now);
        db.DocumentTypes.Add(docType);

        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId,
            "Employment Contract", "Signed copy",
            docType.Id, "contract.pdf", 4096, "application/pdf",
            $"{companyId}/{employeeId}/abc/contract.pdf",
            new DateOnly(2027, 12, 31), uploadedBy, Now);
        db.Documents.Add(doc);

        var empDoc = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, doc.Id, uploadedBy, Now);
        if (acknowledged)
            empDoc.Acknowledge(Now);
        db.EmployeeDocuments.Add(empDoc);

        await db.SaveChangesAsync();
        return (docType, doc, empDoc);
    }

    [Fact]
    public async Task HandleAsync_Returns_EmployeeDocument_Metadata_Only()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var uploadedBy     = Guid.NewGuid();
        var (docType, doc, empDoc) = await SeedAll(db, companyId, employeeId, uploadedBy);
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = empDoc.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var r = result.Value!;
        Assert.Equal(empDoc.Id,              r.EmployeeDocumentId);
        Assert.Equal(doc.Id,                 r.DocumentId);
        Assert.Equal(companyId,              r.CompanyId);
        Assert.Equal(employeeId,             r.EmployeeId);
        Assert.Equal("Employment Contract",  r.Title);
        Assert.Equal("Signed copy",          r.Description);
        Assert.Equal("contract.pdf",         r.FileName);
        Assert.Equal(4096L,                  r.FileSize);
        Assert.Equal("application/pdf",      r.ContentType);
        Assert.Equal(docType.Id,             r.DocumentTypeId);
        Assert.Equal("Contract",             r.DocumentTypeName);
        Assert.Equal(DocumentStatus.Active,  r.Status);
        Assert.Equal(new DateOnly(2027, 12, 31), r.DocumentExpiryDate);
        Assert.Equal(uploadedBy,             r.UploadedBy);
        Assert.Equal(uploadedBy,             r.AddedBy);
        Assert.Null(r.IssueDate);
        Assert.Null(r.ExpiryDate);
        Assert.Null(r.AcknowledgedAt);

        // DOC-02: detail response is metadata-only; there must be no download-URL
        // property on the response record (compile-time proof via reflection).
        var responseType = typeof(GetEmployeeDocumentResponse);
        Assert.DoesNotContain(
            responseType.GetProperties(),
            p => p.Name.Contains("DownloadUrl", StringComparison.OrdinalIgnoreCase)
              || p.Name.Contains("Uri", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleAsync_Returns_AcknowledgedAt_When_Document_Is_Acknowledged()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (_, _, empDoc) = await SeedAll(db, companyId, employeeId, Guid.NewGuid(), acknowledged: true);
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = empDoc.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.AcknowledgedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_EmployeeDocumentId_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeDocumentRequest
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
        var (_, _, empDoc) = await SeedAll(db, companyId, employeeId, Guid.NewGuid());
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeDocumentRequest
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
        var (_, _, empDoc) = await SeedAll(db, companyId, employeeId, Guid.NewGuid());
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeDocumentRequest
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
    public async Task HandleAsync_Does_Not_Require_A_DocumentStorageService_Dependency()
    {
        // DOC-02: the handler must no longer depend on IDocumentStorageService to
        // resolve a download URL - the constructor only accepts the DbContext.
        var ctor = typeof(GetEmployeeDocumentHandler)
            .GetConstructors(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)
            .Single();

        Assert.Single(ctor.GetParameters());
        Assert.Equal(typeof(DocumentsDbContext), ctor.GetParameters()[0].ParameterType);
    }

    // DOC-04: archived (soft-deleted) employee documents must behave as not-found through the
    // normal get-detail endpoint.
    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Is_Archived()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (_, _, empDoc) = await SeedAll(db, companyId, employeeId, Guid.NewGuid());
        empDoc.Archive(Guid.NewGuid(), null, Now);
        await db.SaveChangesAsync();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = empDoc.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
