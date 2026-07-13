using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ListSharedCompanyDocuments;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ListSharedCompanyDocumentsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeEmployeeNameReader(Dictionary<Guid, string>? names = null) : IEmployeeNameReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(names ?? new Dictionary<Guid, string>());
    }

    [Fact]
    public async Task HandleAsync_Returns_Documents_Ordered_By_Most_Recently_Created()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var older = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Older Policy", null, category.Id,
            "key/older.pdf", "older.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now);
        var newer = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Newer Policy", null, category.Id,
            "key/newer.pdf", "newer.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now.AddMinutes(5));

        db.SharedCompanyDocuments.AddRange(older, newer);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Newer Policy", "Older Policy"], result.Value!.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task HandleAsync_Maps_Category_And_UpdatedBy_Names()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId, "Handbook");
        var uploadedBy = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Employee Handbook", null, category.Id,
            "key/handbook.pdf", "handbook.pdf", 100, "application/pdf", null, null, uploadedBy, Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var names  = new Dictionary<Guid, string> { [uploadedBy] = "Laura Bennett" };
        var result = await Handler(db, names).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal("Handbook",      result.Value!.Items[0].CategoryName);
        Assert.Equal("Laura Bennett", result.Value.Items[0].UpdatedByName);
        Assert.Equal(Now,             result.Value.Items[0].UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Includes_Draft_Documents()
    {
        // The list endpoint backs the HR management screen, so drafts must be visible there —
        // unlike the employee-facing "published documents" list.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var draft = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Draft Policy", null, category.Id,
            "key/draft.pdf", "draft.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(draft);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Draft", result.Value.Items[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Documents_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyA  = Guid.NewGuid();
        var companyB  = Guid.NewGuid();
        var categoryA = await SeedCategory(db, companyA);
        var categoryB = await SeedCategory(db, companyB);

        db.SharedCompanyDocuments.AddRange(
            SharedCompanyDocument.Create(Guid.NewGuid(), companyA, "A Policy", null, categoryA.Id,
                "key/a.pdf", "a.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now),
            SharedCompanyDocument.Create(Guid.NewGuid(), companyB, "B Policy", null, categoryB.Id,
                "key/b.pdf", "b.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("A Policy", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_Company_Has_No_Documents()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var draft     = SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "Draft Doc", null, category.Id,
            "key/d.pdf", "d.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now);
        var published = SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "Published Doc", null, category.Id,
            "key/p.pdf", "p.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now);
        published.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(draft, published);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, Status = SharedCompanyDocumentStatus.Published },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Published Doc", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_CategoryId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var policy    = await SeedCategory(db, companyId, "Policy");
        var handbook  = await SeedCategory(db, companyId, "Handbook");

        db.SharedCompanyDocuments.AddRange(
            SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "Policy Doc", null, policy.Id,
                "key/p.pdf", "p.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now),
            SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "Handbook Doc", null, handbook.Id,
                "key/h.pdf", "h.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, CategoryId = handbook.Id },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Handbook Doc", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_ReviewDate_Range()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var dueSoon = SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "Due Soon", null, category.Id,
            "key/s.pdf", "s.pdf", 100, "application/pdf", null, new DateOnly(2026, 8, 1), Guid.NewGuid(), Now);
        var dueLater = SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "Due Later", null, category.Id,
            "key/l.pdf", "l.pdf", 100, "application/pdf", null, new DateOnly(2027, 1, 1), Guid.NewGuid(), Now);
        var noReviewDate = SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "No Review Date", null, category.Id,
            "key/n.pdf", "n.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(dueSoon, dueLater, noReviewDate);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest
            {
                CompanyId      = companyId,
                ReviewDateFrom = new DateOnly(2026, 1, 1),
                ReviewDateTo   = new DateOnly(2026, 12, 31),
            },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Due Soon", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Search_Title_Case_Insensitive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        db.SharedCompanyDocuments.AddRange(
            SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "Remote Working Policy", null, category.Id,
                "key/r.pdf", "r.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now),
            SharedCompanyDocument.Create(Guid.NewGuid(), companyId, "Health and Safety Guide", null, category.Id,
                "key/h.pdf", "h.pdf", 100, "application/pdf", null, null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, Search = "remote" },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Remote Working Policy", result.Value.Items[0].Title);
    }

    private static ListSharedCompanyDocumentsHandler Handler(DocumentsDbContext db, Dictionary<Guid, string>? names = null) =>
        new(db, new FakeEmployeeNameReader(names));

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
