using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.CreateDocumentRequestsOnEmployeeCreated;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class CreateDocumentRequestsOnEmployeeCreatedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 29, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static EmployeeCreatedHandler BuildHandler(
        DocumentsDbContext db,
        IReadOnlyList<PositionProfileRequiredDocumentItem> requiredDocs) =>
        new(db, new FakePositionProfileDocumentsReader(requiredDocs), new FakeClock(FixedUtcNow));

    private static EmployeeCreatedIntegrationEvent MakeEvent(
        Guid companyId,
        Guid employeeId,
        Guid? positionProfileId = null) =>
        new(companyId, employeeId, new DateOnly(2026, 7, 1), null, new DateOnly(2027, 1, 1), positionProfileId);

    [Fact]
    public async Task HandleAsync_Creates_DocumentRequests_For_All_Active_Required_Documents()
    {
        await using var db = BuildContext();
        var companyId         = Guid.NewGuid();
        var employeeId        = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var docTypeId1        = Guid.NewGuid();
        var docTypeId2        = Guid.NewGuid();

        var required = new[]
        {
            new PositionProfileRequiredDocumentItem(docTypeId1, IsMandatory: true,  DueDaysAfterStart: 30, RequiresExpiryDate: false),
            new PositionProfileRequiredDocumentItem(docTypeId2, IsMandatory: false, DueDaysAfterStart: null, RequiresExpiryDate: true),
        };

        var handler = BuildHandler(db, required);
        await handler.HandleAsync(MakeEvent(companyId, employeeId, positionProfileId), CancellationToken.None);

        var requests = await db.DocumentRequests.ToListAsync();
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.Equal(companyId, r.CompanyId));
        Assert.All(requests, r => Assert.Equal(employeeId, r.EmployeeId));
        Assert.All(requests, r => Assert.Equal(DocumentRequestStatus.Pending, r.Status));
    }

    [Fact]
    public async Task HandleAsync_Skips_When_PositionProfileId_Is_Null()
    {
        await using var db = BuildContext();
        var handler = BuildHandler(db, []);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), positionProfileId: null), CancellationToken.None);

        Assert.Empty(await db.DocumentRequests.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Skips_When_No_Required_Documents_Configured()
    {
        await using var db = BuildContext();
        var handler = BuildHandler(db, []);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(await db.DocumentRequests.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Preserves_IsMandatory_Flag()
    {
        await using var db    = BuildContext();
        var docTypeId         = Guid.NewGuid();
        var required          = new[] { new PositionProfileRequiredDocumentItem(docTypeId, IsMandatory: true, DueDaysAfterStart: null, RequiresExpiryDate: false) };
        var handler           = BuildHandler(db, required);
        var employeeId        = Guid.NewGuid();

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), employeeId, Guid.NewGuid()), CancellationToken.None);

        var request = await db.DocumentRequests.SingleAsync();
        Assert.True(request.IsMandatory);
    }

    [Fact]
    public async Task HandleAsync_Preserves_Optional_Document_Flag()
    {
        await using var db = BuildContext();
        var docTypeId      = Guid.NewGuid();
        var required       = new[] { new PositionProfileRequiredDocumentItem(docTypeId, IsMandatory: false, DueDaysAfterStart: null, RequiresExpiryDate: false) };
        var handler        = BuildHandler(db, required);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var request = await db.DocumentRequests.SingleAsync();
        Assert.False(request.IsMandatory);
    }

    [Fact]
    public async Task HandleAsync_Preserves_DueDaysAfterStart()
    {
        await using var db = BuildContext();
        var docTypeId      = Guid.NewGuid();
        var required       = new[] { new PositionProfileRequiredDocumentItem(docTypeId, IsMandatory: true, DueDaysAfterStart: 14, RequiresExpiryDate: false) };
        var handler        = BuildHandler(db, required);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var request = await db.DocumentRequests.SingleAsync();
        Assert.Equal(14, request.DueDaysAfterStart);
    }

    [Fact]
    public async Task HandleAsync_Preserves_RequiresExpiryDate()
    {
        await using var db = BuildContext();
        var docTypeId      = Guid.NewGuid();
        var required       = new[] { new PositionProfileRequiredDocumentItem(docTypeId, IsMandatory: true, DueDaysAfterStart: null, RequiresExpiryDate: true) };
        var handler        = BuildHandler(db, required);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var request = await db.DocumentRequests.SingleAsync();
        Assert.True(request.RequiresExpiryDate);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Create_Duplicate_For_Same_DocumentType()
    {
        await using var db        = BuildContext();
        var companyId             = Guid.NewGuid();
        var employeeId            = Guid.NewGuid();
        var docTypeId             = Guid.NewGuid();
        var required              = new[] { new PositionProfileRequiredDocumentItem(docTypeId, IsMandatory: true, DueDaysAfterStart: null, RequiresExpiryDate: false) };
        var handler               = BuildHandler(db, required);
        var evt                   = MakeEvent(companyId, employeeId, Guid.NewGuid());

        await handler.HandleAsync(evt, CancellationToken.None);
        await handler.HandleAsync(evt, CancellationToken.None);

        Assert.Single(await db.DocumentRequests.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Creates_Requests_For_Different_Employees_Independently()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var docTypeId      = Guid.NewGuid();
        var required       = new[] { new PositionProfileRequiredDocumentItem(docTypeId, IsMandatory: true, DueDaysAfterStart: null, RequiresExpiryDate: false) };
        var handler        = BuildHandler(db, required);

        await handler.HandleAsync(MakeEvent(companyId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        await handler.HandleAsync(MakeEvent(companyId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, await db.DocumentRequests.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Sets_CreatedAt_From_Clock()
    {
        await using var db = BuildContext();
        var docTypeId      = Guid.NewGuid();
        var required       = new[] { new PositionProfileRequiredDocumentItem(docTypeId, IsMandatory: true, DueDaysAfterStart: null, RequiresExpiryDate: false) };
        var handler        = BuildHandler(db, required);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var request = await db.DocumentRequests.SingleAsync();
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), request.CreatedAt);
    }
}
