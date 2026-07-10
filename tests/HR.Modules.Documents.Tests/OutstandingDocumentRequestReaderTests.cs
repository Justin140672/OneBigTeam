using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class OutstandingDocumentRequestReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static DocumentType SeedDocumentType(DocumentsDbContext db, Guid companyId, string name = "Passport")
    {
        var dt = DocumentType.Create(Guid.NewGuid(), companyId, name, null, Now);
        db.DocumentTypes.Add(dt);
        return dt;
    }

    private static DocumentRequest SeedRequest(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        Guid documentTypeId,
        DateOnly? dueDate = null,
        bool isMandatory = true)
    {
        var request = DocumentRequest.Create(
            Guid.NewGuid(), companyId, employeeId, documentTypeId,
            null, dueDate, isMandatory, null, null, Now);
        db.DocumentRequests.Add(request);
        return request;
    }

    [Fact]
    public async Task GetOutstandingRequestsAsync_Returns_Only_Requested_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dt = SeedDocumentType(db, companyId);

        var requested = SeedRequest(db, companyId, employeeId, dt.Id);

        var uploaded = SeedRequest(db, companyId, employeeId, dt.Id);
        uploaded.MarkUploaded(employeeId, Now);

        var cancelled = SeedRequest(db, companyId, employeeId, dt.Id);
        cancelled.Cancel(Now);

        var expired = SeedRequest(db, companyId, employeeId, dt.Id);
        typeof(DocumentRequest).GetProperty(nameof(DocumentRequest.Status))!
            .SetValue(expired, DocumentRequestStatus.Expired);

        await db.SaveChangesAsync();

        var reader = new OutstandingDocumentRequestReader(db);
        var result = await reader.GetOutstandingRequestsAsync(companyId, employeeId, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(requested.Id, result[0].Id);
    }

    [Fact]
    public async Task GetOutstandingRequestsAsync_Is_Scoped_By_CompanyId()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dtA = SeedDocumentType(db, companyA);
        var dtB = SeedDocumentType(db, companyB);

        SeedRequest(db, companyA, employeeId, dtA.Id);
        SeedRequest(db, companyB, employeeId, dtB.Id);
        await db.SaveChangesAsync();

        var reader = new OutstandingDocumentRequestReader(db);
        var result = await reader.GetOutstandingRequestsAsync(companyA, employeeId, CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetOutstandingRequestsAsync_Is_Scoped_By_EmployeeId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var dt = SeedDocumentType(db, companyId);

        SeedRequest(db, companyId, employeeA, dt.Id);
        SeedRequest(db, companyId, employeeB, dt.Id);
        await db.SaveChangesAsync();

        var reader = new OutstandingDocumentRequestReader(db);
        var result = await reader.GetOutstandingRequestsAsync(companyId, employeeA, CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetOutstandingRequestsAsync_Returns_Empty_List_When_None_Exist()
    {
        await using var db = BuildContext();

        var reader = new OutstandingDocumentRequestReader(db);
        var result = await reader.GetOutstandingRequestsAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOutstandingRequestsAsync_Maps_DocumentTypeName_DueDate_And_IsMandatory()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dt = SeedDocumentType(db, companyId, "Right To Work");
        var dueDate = new DateOnly(2026, 8, 1);

        var request = SeedRequest(db, companyId, employeeId, dt.Id, dueDate: dueDate, isMandatory: true);
        await db.SaveChangesAsync();

        var reader = new OutstandingDocumentRequestReader(db);
        var result = await reader.GetOutstandingRequestsAsync(companyId, employeeId, CancellationToken.None);

        Assert.Single(result);
        var item = result[0];
        Assert.Equal(request.Id, item.Id);
        Assert.Equal("Right To Work", item.DocumentTypeName);
        Assert.Equal(dueDate, item.DueDate);
        Assert.True(item.IsMandatory);
    }
}
