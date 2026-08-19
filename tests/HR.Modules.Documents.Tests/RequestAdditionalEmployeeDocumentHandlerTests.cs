using HR.Modules.Tasks.Contracts;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.RequestAdditionalEmployeeDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class RequestAdditionalEmployeeDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 29, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static (RequestAdditionalEmployeeDocumentHandler Handler, FakeTaskCreator Tasks, FakeAuditPublisher Audit)
        BuildHandler(DocumentsDbContext db)
    {
        var tasks = new FakeTaskCreator();
        var audit = new FakeAuditPublisher();
        var handler = new RequestAdditionalEmployeeDocumentHandler(
            db, tasks, new FakeClock(FixedUtcNow), audit);
        return (handler, tasks, audit);
    }

    private static async Task<DocumentType> SeedDocumentTypeAsync(
        DocumentsDbContext db, Guid companyId, string name = "Passport", bool isActive = true)
    {
        var dt = DocumentType.Create(Guid.NewGuid(), companyId, name, null, DateTimeOffset.UtcNow);
        if (!isActive) dt.Deactivate(DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(dt);
        await db.SaveChangesAsync();
        return dt;
    }

    // ── Happy path ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Returns_Success_With_Created_Request()
    {
        await using var db  = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var requestedBy     = Guid.NewGuid();
        var docType         = await SeedDocumentTypeAsync(db, companyId, "Passport");
        var (handler, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new RequestAdditionalEmployeeDocumentRequest
            {
                CompanyId      = companyId,
                EmployeeId     = employeeId,
                DocumentTypeId = docType.Id,
                DueDate        = new DateOnly(2026, 9, 1),
            },
            requestedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId,      result.Value!.CompanyId);
        Assert.Equal(employeeId,     result.Value.EmployeeId);
        Assert.Equal(docType.Id,     result.Value.DocumentTypeId);
        Assert.Equal("Passport",     result.Value.DocumentTypeName);
        Assert.Equal("Requested",    result.Value.Status);
        Assert.Equal(new DateOnly(2026, 9, 1), result.Value.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Persists_DocumentRequest_To_Db()
    {
        await using var db  = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var docType         = await SeedDocumentTypeAsync(db, companyId);
        var (handler, _, _) = BuildHandler(db);

        await handler.HandleAsync(
            new RequestAdditionalEmployeeDocumentRequest
            {
                CompanyId = companyId, EmployeeId = employeeId, DocumentTypeId = docType.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        var req = Assert.Single(await db.DocumentRequests.ToListAsync());
        Assert.Equal(companyId,  req.CompanyId);
        Assert.Equal(employeeId, req.EmployeeId);
        Assert.Equal(docType.Id, req.DocumentTypeId);
        Assert.Equal(DocumentRequestStatus.Requested, req.Status);
    }

    [Fact]
    public async Task HandleAsync_Creates_Upload_Task_Assigned_To_Employee()
    {
        await using var db    = BuildContext();
        var companyId         = Guid.NewGuid();
        var employeeId        = Guid.NewGuid();
        var docType           = await SeedDocumentTypeAsync(db, companyId, "Driving Licence");
        var (handler, tasks, _) = BuildHandler(db);

        await handler.HandleAsync(
            new RequestAdditionalEmployeeDocumentRequest
            {
                CompanyId = companyId, EmployeeId = employeeId, DocumentTypeId = docType.Id,
                DueDate   = new DateOnly(2026, 9, 30),
            },
            Guid.NewGuid(), CancellationToken.None);

        var task = Assert.Single(tasks.Created);
        Assert.Equal(companyId,              task.CompanyId);
        Assert.Equal(employeeId,             task.AssignedEmployeeId);
        Assert.Equal(TaskSource.Document,    task.Source);
        Assert.Equal(TaskActionType.Upload,  task.ActionType);
        Assert.Equal(new DateOnly(2026, 9, 30), task.DueDate);
        Assert.Contains("Driving Licence",   task.Title);
    }

    [Fact]
    public async Task HandleAsync_Task_SourceEntityId_Is_DocumentRequest_Id()
    {
        await using var db     = BuildContext();
        var companyId          = Guid.NewGuid();
        var employeeId         = Guid.NewGuid();
        var docType            = await SeedDocumentTypeAsync(db, companyId);
        var (handler, tasks, _) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new RequestAdditionalEmployeeDocumentRequest
            {
                CompanyId = companyId, EmployeeId = employeeId, DocumentTypeId = docType.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(result.Value!.DocumentRequestId, tasks.Created[0].SourceEntityId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event()
    {
        await using var db      = BuildContext();
        var companyId           = Guid.NewGuid();
        var employeeId          = Guid.NewGuid();
        var requestedBy         = Guid.NewGuid();
        var docType             = await SeedDocumentTypeAsync(db, companyId, "Certificate");
        var (handler, _, audit) = BuildHandler(db);

        await handler.HandleAsync(
            new RequestAdditionalEmployeeDocumentRequest
            {
                CompanyId = companyId, EmployeeId = employeeId, DocumentTypeId = docType.Id,
            },
            requestedBy, CancellationToken.None);

        var evt = Assert.Single(audit.Published.OfType<DocumentRequestedAuditEvent>());
        Assert.Equal(companyId,   evt.CompanyId);
        Assert.Equal(employeeId,  evt.EmployeeId);
        Assert.Equal(requestedBy, evt.RequestedBy);
        Assert.Equal("Certificate", evt.DocumentTypeName);
    }

    // ── Failure paths ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DocumentType_Does_Not_Exist()
    {
        await using var db  = BuildContext();
        var (handler, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new RequestAdditionalEmployeeDocumentRequest
            {
                CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), DocumentTypeId = Guid.NewGuid(),
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DocumentType_Is_Inactive()
    {
        await using var db  = BuildContext();
        var companyId       = Guid.NewGuid();
        var docType         = await SeedDocumentTypeAsync(db, companyId, isActive: false);
        var (handler, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new RequestAdditionalEmployeeDocumentRequest
            {
                CompanyId = companyId, EmployeeId = Guid.NewGuid(), DocumentTypeId = docType.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Request_Already_Exists()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var docType        = await SeedDocumentTypeAsync(db, companyId);

        // Seed an existing request for the same employee+type
        db.DocumentRequests.Add(DocumentRequest.Create(
            Guid.NewGuid(), companyId, employeeId, docType.Id,
            null, null, false, null, null, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var (handler, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new RequestAdditionalEmployeeDocumentRequest
            {
                CompanyId = companyId, EmployeeId = employeeId, DocumentTypeId = docType.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_DocumentType_For_Different_Employees()
    {
        await using var db  = BuildContext();
        var companyId       = Guid.NewGuid();
        var docType         = await SeedDocumentTypeAsync(db, companyId);
        var (handler, _, _) = BuildHandler(db);

        var r1 = await handler.HandleAsync(
            new RequestAdditionalEmployeeDocumentRequest
            {
                CompanyId = companyId, EmployeeId = Guid.NewGuid(), DocumentTypeId = docType.Id,
            }, Guid.NewGuid(), CancellationToken.None);

        var r2 = await handler.HandleAsync(
            new RequestAdditionalEmployeeDocumentRequest
            {
                CompanyId = companyId, EmployeeId = Guid.NewGuid(), DocumentTypeId = docType.Id,
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);
        Assert.Equal(2, await db.DocumentRequests.CountAsync());
    }
}
