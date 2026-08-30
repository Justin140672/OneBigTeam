using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class OutstandingDocumentRequestComplianceReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static DocumentRequest Seed(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        string typeName = "Passport",
        DateOnly? dueDate = null,
        bool isMandatory = true)
    {
        var dt = DocumentType.Create(Guid.NewGuid(), companyId, typeName, null, Now);
        db.DocumentTypes.Add(dt);
        var request = DocumentRequest.Create(
            Guid.NewGuid(), companyId, employeeId, dt.Id, null, dueDate, isMandatory, null, null, Now);
        db.DocumentRequests.Add(request);
        return request;
    }

    private static async Task<IReadOnlyList<HR.Infrastructure.Abstractions.OutstandingDocumentRequestComplianceItem>> Read(
        DocumentsDbContext db, Guid companyId) =>
        await new OutstandingDocumentRequestComplianceReader(db)
            .GetOutstandingDocumentRequestsAsync(companyId, CancellationToken.None);

    [Fact]
    public async Task Returns_Only_Requested_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var requested = Seed(db, companyId, employeeId);

        var uploaded = Seed(db, companyId, employeeId);
        uploaded.MarkUploaded(employeeId, Now);

        var cancelled = Seed(db, companyId, employeeId);
        cancelled.Cancel(Now);

        await db.SaveChangesAsync();

        var result = await Read(db, companyId);

        var item = Assert.Single(result);
        Assert.Equal(requested.Id, item.RequestId);
    }

    [Fact]
    public async Task Is_Scoped_By_CompanyId()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        Seed(db, companyA, Guid.NewGuid());
        Seed(db, companyB, Guid.NewGuid());
        await db.SaveChangesAsync();

        Assert.Single(await Read(db, companyA));
    }

    [Fact]
    public async Task Surfaces_EmployeeId_TypeName_DueDate_And_IsMandatory()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dueDate = new DateOnly(2026, 9, 15);
        Seed(db, companyId, employeeId, "Right to Work", dueDate, isMandatory: true);
        await db.SaveChangesAsync();

        var item = Assert.Single(await Read(db, companyId));
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal("Right to Work", item.DocumentTypeName);
        Assert.Equal(dueDate, item.DueDate);
        Assert.True(item.IsMandatory);
    }

    [Fact]
    public async Task Surfaces_Null_DueDate_And_NonMandatory_Flag()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        Seed(db, companyId, Guid.NewGuid(), dueDate: null, isMandatory: false);
        await db.SaveChangesAsync();

        var item = Assert.Single(await Read(db, companyId));
        Assert.Null(item.DueDate);
        Assert.False(item.IsMandatory);
    }

    [Fact]
    public async Task Returns_Empty_When_None_Exist()
    {
        await using var db = BuildContext();
        Assert.Empty(await Read(db, Guid.NewGuid()));
    }
}
