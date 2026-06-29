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
    private static readonly DateOnly StartDate = new(2026, 7, 1);

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
        new(companyId, employeeId, StartDate, null, new DateOnly(2027, 1, 1), positionProfileId);

    private static PositionProfileRequiredDocumentItem MakeDoc(
        Guid documentTypeId,
        int? dueDaysAfterStart = null,
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), documentTypeId, IsMandatory: true,
            DueDaysAfterStart: dueDaysAfterStart, RequiresExpiryDate: false);

    [Fact]
    public async Task HandleAsync_Creates_DocumentRequests_For_All_Active_Required_Documents()
    {
        await using var db = BuildContext();
        var companyId         = Guid.NewGuid();
        var employeeId        = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var required = new[]
        {
            MakeDoc(Guid.NewGuid(), dueDaysAfterStart: 30),
            MakeDoc(Guid.NewGuid(), dueDaysAfterStart: null),
        };

        var handler = BuildHandler(db, required);
        await handler.HandleAsync(MakeEvent(companyId, employeeId, positionProfileId), CancellationToken.None);

        var requests = await db.DocumentRequests.ToListAsync();
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.Equal(companyId, r.CompanyId));
        Assert.All(requests, r => Assert.Equal(employeeId, r.EmployeeId));
        Assert.All(requests, r => Assert.Equal(DocumentRequestStatus.Requested, r.Status));
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
    public async Task HandleAsync_Calculates_DueDate_From_StartDate_And_DueDaysAfterStart()
    {
        await using var db = BuildContext();
        var docTypeId  = Guid.NewGuid();
        var required   = new[] { MakeDoc(docTypeId, dueDaysAfterStart: 30) };
        var handler    = BuildHandler(db, required);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var request = await db.DocumentRequests.SingleAsync();
        Assert.Equal(StartDate.AddDays(30), request.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Sets_DueDate_Null_When_DueDaysAfterStart_Is_Null()
    {
        await using var db = BuildContext();
        var docTypeId  = Guid.NewGuid();
        var required   = new[] { MakeDoc(docTypeId, dueDaysAfterStart: null) };
        var handler    = BuildHandler(db, required);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var request = await db.DocumentRequests.SingleAsync();
        Assert.Null(request.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Sets_PositionProfileRequiredDocumentId()
    {
        await using var db = BuildContext();
        var docTypeId = Guid.NewGuid();
        var reqDocId  = Guid.NewGuid();
        var required  = new[] { MakeDoc(docTypeId, id: reqDocId) };
        var handler   = BuildHandler(db, required);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var request = await db.DocumentRequests.SingleAsync();
        Assert.Equal(reqDocId, request.PositionProfileRequiredDocumentId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Create_Duplicate_For_Same_DocumentType()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var docTypeId  = Guid.NewGuid();
        var required   = new[] { MakeDoc(docTypeId) };
        var handler    = BuildHandler(db, required);
        var evt        = MakeEvent(companyId, employeeId, Guid.NewGuid());

        await handler.HandleAsync(evt, CancellationToken.None);
        await handler.HandleAsync(evt, CancellationToken.None);

        Assert.Single(await db.DocumentRequests.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Creates_Requests_For_Different_Employees_Independently()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var docTypeId  = Guid.NewGuid();
        var required   = new[] { MakeDoc(docTypeId) };
        var handler    = BuildHandler(db, required);

        await handler.HandleAsync(MakeEvent(companyId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        await handler.HandleAsync(MakeEvent(companyId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, await db.DocumentRequests.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Sets_CreatedAt_From_Clock()
    {
        await using var db = BuildContext();
        var required   = new[] { MakeDoc(Guid.NewGuid()) };
        var handler    = BuildHandler(db, required);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var request = await db.DocumentRequests.SingleAsync();
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), request.CreatedAt);
    }
}
