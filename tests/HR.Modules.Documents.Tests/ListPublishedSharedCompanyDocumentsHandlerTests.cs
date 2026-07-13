using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ListPublishedSharedCompanyDocuments;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ListPublishedSharedCompanyDocumentsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Excludes_Draft_And_Archived_Documents()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var draft = CreateDoc(companyId, "Draft Doc", category.Id, "key/d.pdf", "d.pdf", Guid.NewGuid());

        var archived = CreateDoc(companyId, "Archived Doc", category.Id, "key/a.pdf", "a.pdf", Guid.NewGuid());
        archived.Publish(Guid.NewGuid(), Now);
        archived.Archive(Guid.NewGuid(), Now);

        var published = CreateDoc(companyId, "Published Doc", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid());
        published.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(draft, archived, published);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Published Doc", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Includes_AllEmployees_Audience_Documents_For_Any_Caller()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Published Doc", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid(),
            audienceDepartmentId: null, audienceLocationId: null);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Includes_Department_Scoped_Document_For_Caller_In_That_Department()
    {
        await using var db = BuildContext();
        var companyId   = Guid.NewGuid();
        var category    = await SeedCategory(db, companyId);
        var caller       = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Engineering Policy", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid(),
            audienceDepartmentId: departmentId);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.EmployeeAudiences[caller] = (departmentId, null);

        var result = await Handler(db, audienceReader).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Department_Scoped_Document_For_Caller_In_A_Different_Department()
    {
        await using var db = BuildContext();
        var companyId   = Guid.NewGuid();
        var category    = await SeedCategory(db, companyId);
        var caller       = Guid.NewGuid();
        var engineeringId = Guid.NewGuid();
        var salesId       = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Engineering Policy", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid(),
            audienceDepartmentId: engineeringId);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.EmployeeAudiences[caller] = (salesId, null);

        var result = await Handler(db, audienceReader).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_Title()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var b = CreateDoc(companyId, "B Policy", category.Id, "key/b.pdf", "b.pdf", Guid.NewGuid());
        b.Publish(Guid.NewGuid(), Now);
        var a = CreateDoc(companyId, "A Policy", category.Id, "key/a.pdf", "a.pdf", Guid.NewGuid());
        a.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(b, a);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Equal(["A Policy", "B Policy"], result.Value!.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task HandleAsync_Excludes_Documents_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyA  = Guid.NewGuid();
        var companyB  = Guid.NewGuid();
        var categoryA = await SeedCategory(db, companyA);
        var categoryB = await SeedCategory(db, companyB);
        var caller    = Guid.NewGuid();

        var docA = CreateDoc(companyA, "A Policy", categoryA.Id, "key/a.pdf", "a.pdf", Guid.NewGuid());
        docA.Publish(Guid.NewGuid(), Now);
        var docB = CreateDoc(companyB, "B Policy", categoryB.Id, "key/b.pdf", "b.pdf", Guid.NewGuid());
        docB.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(docA, docB);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyA }, caller,
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("A Policy", result.Value.Items[0].Title);
    }

    private static ListPublishedSharedCompanyDocumentsHandler Handler(
        DocumentsDbContext db, FakeEmployeeAudienceReader? audienceReader = null) =>
        new(db, audienceReader ?? new FakeEmployeeAudienceReader());

    private static SharedCompanyDocument CreateDoc(
        Guid companyId, string title, Guid categoryId, string storageKey, string fileName, Guid createdBy,
        Guid? audienceDepartmentId = null, Guid? audienceLocationId = null) =>
        SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, title, null, categoryId, storageKey, fileName, 100, "application/pdf",
            null, null, audienceDepartmentId, audienceLocationId, false, createdBy, Now);

    private static async Task<CompanyDocumentCategory> SeedCategory(
        DocumentsDbContext db, Guid companyId, string name = "Policy")
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, name, Now);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
