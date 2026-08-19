using HR.Modules.Tasks.Contracts;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.CreateDocumentRequestsOnEmployeeCreated;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
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

    private static (EmployeeCreatedHandler Handler, FakeTaskCreator TaskCreator) BuildHandler(
        DocumentsDbContext db,
        IReadOnlyList<PositionProfileRequiredDocumentItem> requiredDocs,
        IReadOnlyDictionary<Guid, string>? typeNames = null)
    {
        var taskCreator = new FakeTaskCreator();
        var handler = new EmployeeCreatedHandler(
            db,
            new FakePositionProfileDocumentsReader(requiredDocs),
            new FakeDocumentTypeReader(typeNames ?? new Dictionary<Guid, string>()),
            taskCreator,
            new FakeClock(FixedUtcNow));
        return (handler, taskCreator);
    }

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

    // ── DocumentRequest creation ─────────────────────────────────────────────────

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

        var (handler, _) = BuildHandler(db, required);
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
        var (handler, _) = BuildHandler(db, []);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), positionProfileId: null), CancellationToken.None);

        Assert.Empty(await db.DocumentRequests.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Skips_When_No_Required_Documents_Configured()
    {
        await using var db = BuildContext();
        var (handler, _) = BuildHandler(db, []);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(await db.DocumentRequests.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Calculates_DueDate_From_StartDate_And_DueDaysAfterStart()
    {
        await using var db = BuildContext();
        var docTypeId  = Guid.NewGuid();
        var (handler, _) = BuildHandler(db, [MakeDoc(docTypeId, dueDaysAfterStart: 30)]);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var request = await db.DocumentRequests.SingleAsync();
        Assert.Equal(StartDate.AddDays(30), request.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Sets_DueDate_Null_When_DueDaysAfterStart_Is_Null()
    {
        await using var db = BuildContext();
        var (handler, _) = BuildHandler(db, [MakeDoc(Guid.NewGuid(), dueDaysAfterStart: null)]);

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
        var (handler, _) = BuildHandler(db, [MakeDoc(docTypeId, id: reqDocId)]);

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
        var (handler, _) = BuildHandler(db, [MakeDoc(Guid.NewGuid())]);
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
        var (handler, _) = BuildHandler(db, [MakeDoc(Guid.NewGuid())]);

        await handler.HandleAsync(MakeEvent(companyId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        await handler.HandleAsync(MakeEvent(companyId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, await db.DocumentRequests.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Sets_CreatedAt_From_Clock()
    {
        await using var db = BuildContext();
        var (handler, _) = BuildHandler(db, [MakeDoc(Guid.NewGuid())]);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var request = await db.DocumentRequests.SingleAsync();
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), request.CreatedAt);
    }

    // ── Task creation ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Creates_One_Upload_Task_Per_DocumentRequest()
    {
        await using var db = BuildContext();
        var docTypeId1 = Guid.NewGuid();
        var docTypeId2 = Guid.NewGuid();
        var typeNames  = new Dictionary<Guid, string> { [docTypeId1] = "Passport", [docTypeId2] = "Right To Work" };
        var (handler, taskCreator) = BuildHandler(db,
            [MakeDoc(docTypeId1), MakeDoc(docTypeId2)],
            typeNames);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, taskCreator.Created.Count);
    }

    [Fact]
    public async Task HandleAsync_Task_Source_Is_Document_And_ActionType_Is_Upload()
    {
        await using var db = BuildContext();
        var docTypeId = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(db, [MakeDoc(docTypeId)],
            new Dictionary<Guid, string> { [docTypeId] = "Passport" });

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(TaskSource.Document,    task.Source);
        Assert.Equal(TaskActionType.Upload,  task.ActionType);
    }

    [Fact]
    public async Task HandleAsync_Task_Is_Assigned_To_Employee()
    {
        await using var db = BuildContext();
        var employeeId = Guid.NewGuid();
        var docTypeId  = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(db, [MakeDoc(docTypeId)],
            new Dictionary<Guid, string> { [docTypeId] = "Passport" });

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), employeeId, Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(employeeId, task.AssignedEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Task_DueDate_Matches_DocumentRequest_DueDate()
    {
        await using var db = BuildContext();
        var docTypeId  = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(db, [MakeDoc(docTypeId, dueDaysAfterStart: 14)],
            new Dictionary<Guid, string> { [docTypeId] = "Contract" });

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(StartDate.AddDays(14), task.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Task_DueDate_Is_Null_When_No_DueDaysAfterStart()
    {
        await using var db = BuildContext();
        var docTypeId  = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(db, [MakeDoc(docTypeId, dueDaysAfterStart: null)],
            new Dictionary<Guid, string> { [docTypeId] = "Certificate" });

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Null(task.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Task_Title_And_Description_Include_DocumentType_Name()
    {
        await using var db = BuildContext();
        var docTypeId  = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(db, [MakeDoc(docTypeId)],
            new Dictionary<Guid, string> { [docTypeId] = "Driving Licence" });

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal("Upload Driving Licence", task.Title);
        Assert.Equal("Please upload a copy of your Driving Licence.", task.Description);
    }

    [Fact]
    public async Task HandleAsync_Task_SourceEntityId_Is_DocumentRequestId()
    {
        await using var db = BuildContext();
        var docTypeId  = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(db, [MakeDoc(docTypeId)],
            new Dictionary<Guid, string> { [docTypeId] = "Passport" });

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var request = await db.DocumentRequests.SingleAsync();
        var task    = Assert.Single(taskCreator.Created);
        Assert.Equal(request.Id, task.SourceEntityId);
    }

    [Fact]
    public async Task HandleAsync_No_Tasks_When_PositionProfileId_Is_Null()
    {
        await using var db = BuildContext();
        var (handler, taskCreator) = BuildHandler(db, []);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), positionProfileId: null), CancellationToken.None);

        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task HandleAsync_No_Tasks_When_No_Required_Documents_Configured()
    {
        await using var db = BuildContext();
        var (handler, taskCreator) = BuildHandler(db, []);

        await handler.HandleAsync(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task HandleAsync_No_Tasks_For_Already_Existing_DocumentType()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var docTypeId  = Guid.NewGuid();
        var (handler, taskCreator) = BuildHandler(db, [MakeDoc(docTypeId)],
            new Dictionary<Guid, string> { [docTypeId] = "Passport" });
        var evt = MakeEvent(companyId, employeeId, Guid.NewGuid());

        await handler.HandleAsync(evt, CancellationToken.None); // first time — creates request + task
        await handler.HandleAsync(evt, CancellationToken.None); // second time — skips duplicate

        Assert.Single(taskCreator.Created); // only one task total
    }
}
