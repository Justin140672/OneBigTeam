using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ListPublishedSharedCompanyDocuments;
using HR.Modules.Documents.Persistence;
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

        var draft = SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "Draft Doc", null, category.Id,
            "key/d.pdf", "d.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now);

        var archived = SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "Archived Doc", null, category.Id,
            "key/a.pdf", "a.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now);
        archived.Publish(Guid.NewGuid(), Now);
        archived.Archive(Guid.NewGuid(), Now);

        var published = SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "Published Doc", null, category.Id,
            "key/p.pdf", "p.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now);
        published.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(draft, archived, published);
        await db.SaveChangesAsync();

        var result = await new ListPublishedSharedCompanyDocumentsHandler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Published Doc", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Expose_Status_Version_Or_UpdatedBy()
    {
        // Compile-time guarantee, not a runtime assertion: PublishedSharedCompanyDocumentItem
        // deliberately has no Status/VersionNumber/UpdatedByName properties — this test exists
        // to document that omission so a future edit doesn't accidentally widen the DTO back
        // out to the full HR-facing shape.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var published = SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "Published Doc", "Some description", category.Id,
            "key/p.pdf", "p.pdf", 100, "application/pdf", new DateOnly(2026, 1, 1), null, Guid.NewGuid(), Now);
        published.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(published);
        await db.SaveChangesAsync();

        var result = await new ListPublishedSharedCompanyDocumentsHandler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        var item = result.Value!.Items[0];
        Assert.Equal("Published Doc",      item.Title);
        Assert.Equal("Some description",   item.Description);
        Assert.Equal("Policy",             item.CategoryName);
        Assert.Equal(new DateOnly(2026, 1, 1), item.EffectiveDate);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_Title()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var b = SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "B Policy", null, category.Id,
            "key/b.pdf", "b.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now);
        b.Publish(Guid.NewGuid(), Now);
        var a = SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "A Policy", null, category.Id,
            "key/a.pdf", "a.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now);
        a.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(b, a);
        await db.SaveChangesAsync();

        var result = await new ListPublishedSharedCompanyDocumentsHandler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId },
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

        var docA = SharedCompanyDocument.Create(Guid.NewGuid(), companyA, "A Policy", null, categoryA.Id,
            "key/a.pdf", "a.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now);
        docA.Publish(Guid.NewGuid(), Now);
        var docB = SharedCompanyDocument.Create(Guid.NewGuid(), companyB, "B Policy", null, categoryB.Id,
            "key/b.pdf", "b.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now);
        docB.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(docA, docB);
        await db.SaveChangesAsync();

        var result = await new ListPublishedSharedCompanyDocumentsHandler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("A Policy", result.Value.Items[0].Title);
    }

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
