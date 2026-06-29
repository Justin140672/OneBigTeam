using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.CancelDocumentRequest;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class CancelDocumentRequestHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 29, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static (CancelDocumentRequestHandler Handler, FakeTaskCanceller Canceller, FakeAuditPublisher Audit)
        BuildHandler(DocumentsDbContext db)
    {
        var canceller = new FakeTaskCanceller();
        var audit     = new FakeAuditPublisher();
        var handler   = new CancelDocumentRequestHandler(db, canceller, new FakeClock(FixedUtcNow), audit);
        return (handler, canceller, audit);
    }

    private static async Task<(DocumentType DocType, DocumentRequest Request)> SeedRequestAsync(
        DocumentsDbContext db, Guid companyId, Guid employeeId)
    {
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Passport", null, DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(docType);

        var request = DocumentRequest.Create(
            Guid.NewGuid(), companyId, employeeId, docType.Id,
            null, null, null, DateTimeOffset.UtcNow);
        db.DocumentRequests.Add(request);

        await db.SaveChangesAsync();
        return (docType, request);
    }

    // ── Happy path ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Returns_Success_For_Requested_Document()
    {
        await using var db   = BuildContext();
        var companyId        = Guid.NewGuid();
        var employeeId       = Guid.NewGuid();
        var (_, req)         = await SeedRequestAsync(db, companyId, employeeId);
        var (handler, _, _)  = BuildHandler(db);

        var result = await handler.HandleAsync(
            new CancelDocumentRequestRequest
            {
                CompanyId = companyId, EmployeeId = employeeId, DocumentRequestId = req.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Sets_Status_To_Cancelled()
    {
        await using var db   = BuildContext();
        var companyId        = Guid.NewGuid();
        var employeeId       = Guid.NewGuid();
        var (_, req)         = await SeedRequestAsync(db, companyId, employeeId);
        var (handler, _, _)  = BuildHandler(db);

        await handler.HandleAsync(
            new CancelDocumentRequestRequest
            {
                CompanyId = companyId, EmployeeId = employeeId, DocumentRequestId = req.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        var updated = await db.DocumentRequests.SingleAsync(r => r.Id == req.Id);
        Assert.Equal(DocumentRequestStatus.Cancelled, updated.Status);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_Cancels_Associated_Upload_Task()
    {
        await using var db       = BuildContext();
        var companyId            = Guid.NewGuid();
        var employeeId           = Guid.NewGuid();
        var (_, req)             = await SeedRequestAsync(db, companyId, employeeId);
        var (handler, canceller, _) = BuildHandler(db);

        await handler.HandleAsync(
            new CancelDocumentRequestRequest
            {
                CompanyId = companyId, EmployeeId = employeeId, DocumentRequestId = req.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        var call = Assert.Single(canceller.Calls);
        Assert.Equal(companyId,             call.CompanyId);
        Assert.Equal(req.Id,                call.SourceEntityId);
        Assert.Equal(TaskSource.Document,   call.Source);
        Assert.Equal(TaskActionType.Upload, call.ActionType);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event()
    {
        await using var db       = BuildContext();
        var companyId            = Guid.NewGuid();
        var employeeId           = Guid.NewGuid();
        var cancelledBy          = Guid.NewGuid();
        var (docType, req)       = await SeedRequestAsync(db, companyId, employeeId);
        var (handler, _, audit)  = BuildHandler(db);

        await handler.HandleAsync(
            new CancelDocumentRequestRequest
            {
                CompanyId = companyId, EmployeeId = employeeId, DocumentRequestId = req.Id,
            },
            cancelledBy, CancellationToken.None);

        var evt = Assert.Single(audit.Published.OfType<DocumentRequestCancelledAuditEvent>());
        Assert.Equal(companyId,        evt.CompanyId);
        Assert.Equal(req.Id,           evt.DocumentRequestId);
        Assert.Equal(employeeId,       evt.EmployeeId);
        Assert.Equal(cancelledBy,      evt.CancelledBy);
        Assert.Equal(docType.Name,     evt.DocumentTypeName);
    }

    // ── Failure paths ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        await using var db   = BuildContext();
        var (handler, _, _)  = BuildHandler(db);

        var result = await handler.HandleAsync(
            new CancelDocumentRequestRequest
            {
                CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), DocumentRequestId = Guid.NewGuid(),
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_EmployeeId_Does_Not_Match()
    {
        await using var db   = BuildContext();
        var companyId        = Guid.NewGuid();
        var (_, req)         = await SeedRequestAsync(db, companyId, Guid.NewGuid());
        var (handler, _, _)  = BuildHandler(db);

        var result = await handler.HandleAsync(
            new CancelDocumentRequestRequest
            {
                CompanyId = companyId, EmployeeId = Guid.NewGuid(), DocumentRequestId = req.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Already_Uploaded()
    {
        await using var db   = BuildContext();
        var companyId        = Guid.NewGuid();
        var employeeId       = Guid.NewGuid();
        var (_, req)         = await SeedRequestAsync(db, companyId, employeeId);

        req.MarkUploaded(employeeId, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var (handler, _, _)  = BuildHandler(db);

        var result = await handler.HandleAsync(
            new CancelDocumentRequestRequest
            {
                CompanyId = companyId, EmployeeId = employeeId, DocumentRequestId = req.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }
}
