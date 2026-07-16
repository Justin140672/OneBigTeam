using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ListSharedCompanyDocumentsDueForReview;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ListSharedCompanyDocumentsDueForReviewHandlerTests
{
    // Today is 2026-07-16.
    private static readonly DateTime FixedUtcNow = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today        = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateTimeOffset Now    = new(FixedUtcNow, TimeSpan.Zero);

    private sealed class FakeEmployeeNameReader(Dictionary<Guid, string>? names = null) : IEmployeeNameReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(names ?? new Dictionary<Guid, string>());
    }

    [Fact]
    public async Task HandleAsync_Includes_Document_With_ReviewDate_In_The_Past()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var doc = CreateDoc(companyId, "Overdue Policy", category.Id, Today.AddDays(-5));
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsDueForReviewRequest(companyId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Overdue Policy", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Includes_Document_With_ReviewDate_Equal_To_Today()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var doc = CreateDoc(companyId, "Due Today Policy", category.Id, Today);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsDueForReviewRequest(companyId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Due Today Policy", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Document_With_ReviewDate_In_The_Future()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var doc = CreateDoc(companyId, "Future Policy", category.Id, Today.AddDays(5));
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsDueForReviewRequest(companyId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Document_With_Null_ReviewDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var doc = CreateDoc(companyId, "No Review Date Policy", category.Id, reviewDate: null);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsDueForReviewRequest(companyId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Archived_Document_Even_When_Overdue()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var doc = CreateDoc(companyId, "Archived Overdue Policy", category.Id, Today.AddDays(-10));
        doc.Archive(Guid.NewGuid(), "No longer needed", Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsDueForReviewRequest(companyId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Includes_Draft_And_Published_Overdue_Documents()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var draft     = CreateDoc(companyId, "Draft Overdue Policy", category.Id, Today.AddDays(-1));
        var published = CreateDoc(companyId, "Published Overdue Policy", category.Id, Today.AddDays(-2));
        published.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(draft, published);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsDueForReviewRequest(companyId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Contains(result.Value.Items, i => i.Title == "Draft Overdue Policy" && i.Status == "Draft");
        Assert.Contains(result.Value.Items, i => i.Title == "Published Overdue Policy" && i.Status == "Published");
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
            CreateDoc(companyA, "Company A Policy", categoryA.Id, Today.AddDays(-1)),
            CreateDoc(companyB, "Company B Policy", categoryB.Id, Today.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsDueForReviewRequest(companyA),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Company A Policy", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_ReviewDate_Ascending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var mostOverdue = CreateDoc(companyId, "Most Overdue", category.Id, Today.AddDays(-10));
        var lessOverdue = CreateDoc(companyId, "Less Overdue", category.Id, Today.AddDays(-1));

        db.SharedCompanyDocuments.AddRange(lessOverdue, mostOverdue);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsDueForReviewRequest(companyId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Most Overdue", "Less Overdue"], result.Value!.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task HandleAsync_Maps_Category_And_ReviewOwner_And_UpdatedBy_Names()
    {
        await using var db = BuildContext();
        var companyId     = Guid.NewGuid();
        var category      = await SeedCategory(db, companyId, "Handbook");
        var updatedBy     = Guid.NewGuid();
        var reviewOwnerId = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Handbook Policy", null, category.Id, "key/handbook.pdf", "handbook.pdf",
            100, "application/pdf", null, Today.AddDays(-1), SharedCompanyDocumentReviewFrequency.Yearly, null,
            reviewOwnerId, false, null, null, updatedBy, Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var names = new Dictionary<Guid, string>
        {
            [updatedBy]     = "Laura Bennett",
            [reviewOwnerId] = "Priya Shah",
        };

        var result = await Handler(db, names).HandleAsync(
            new ListSharedCompanyDocumentsDueForReviewRequest(companyId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Handbook",       item.CategoryName);
        Assert.Equal("Laura Bennett",  item.UpdatedByName);
        Assert.Equal(reviewOwnerId,    item.ReviewOwnerEmployeeId);
        Assert.Equal("Priya Shah",     item.ReviewOwnerName);
        Assert.Equal("Yearly",         item.ReviewFrequency);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_Company_Has_No_Documents()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsDueForReviewRequest(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    private static ListSharedCompanyDocumentsDueForReviewHandler Handler(
        DocumentsDbContext db, Dictionary<Guid, string>? names = null) =>
        new(db, new FakeClock(FixedUtcNow), new FakeEmployeeNameReader(names));

    private static SharedCompanyDocument CreateDoc(
        Guid companyId, string title, Guid categoryId, DateOnly? reviewDate) =>
        SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, title, null, categoryId, $"key/{Guid.NewGuid():N}.pdf", "doc.pdf",
            100, "application/pdf", null, reviewDate, SharedCompanyDocumentReviewFrequency.None, null,
            null, false, null, null, Guid.NewGuid(), Now);

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
