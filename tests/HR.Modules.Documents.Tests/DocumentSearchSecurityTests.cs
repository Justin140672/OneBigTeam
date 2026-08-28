// SEA-08: Search security matrix — document search cross-company isolation,
// consistent out-of-range page behaviour and search term validation.
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ListSharedCompanyDocuments;
using HR.Modules.Documents.Features.SearchEmployeeDocuments;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class DocumentSearchSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

    // ── SearchEmployeeDocuments: cross-company isolation ──────────────────

    [Fact]
    public async Task SearchEmployeeDocuments_TotalCount_Excludes_Other_Company_Records()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var typeA = await SeedType(db, companyA);
        var typeB = await SeedType(db, companyB);

        // 2 docs for company A, 3 for company B
        await SeedDoc(db, companyA, typeA.Id, "A Doc 1", "a1.pdf", Guid.NewGuid(), Now);
        await SeedDoc(db, companyA, typeA.Id, "A Doc 2", "a2.pdf", Guid.NewGuid(), Now);
        await SeedDoc(db, companyB, typeB.Id, "B Doc 1", "b1.pdf", Guid.NewGuid(), Now);
        await SeedDoc(db, companyB, typeB.Id, "B Doc 2", "b2.pdf", Guid.NewGuid(), Now);
        await SeedDoc(db, companyB, typeB.Id, "B Doc 3", "b3.pdf", Guid.NewGuid(), Now);

        var result = await SearchHandler(db).HandleAsync(
            new SearchEmployeeDocumentsRequest { CompanyId = companyA },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.All(result.Value.Items, i => Assert.StartsWith("A Doc", i.Title));
    }

    [Fact]
    public async Task SearchEmployeeDocuments_AllowedEmployeeIds_Restricts_Results_And_TotalCount()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);
        var allowed    = Guid.NewGuid();
        var notAllowed = Guid.NewGuid();

        await SeedDoc(db, companyId, type.Id, "Allowed", "allowed.pdf", allowed, Now);
        await SeedDoc(db, companyId, type.Id, "Not Allowed", "notallowed.pdf", notAllowed, Now);
        await SeedDoc(db, companyId, type.Id, "Also Allowed", "also.pdf", allowed, Now);

        var result = await SearchHandler(db).HandleAsync(
            new SearchEmployeeDocumentsRequest { CompanyId = companyId },
            allowedEmployeeIds: [allowed], callerIsHrAdministrator: false, CancellationToken.None);

        // TotalCount must reflect only the accessible records, not all company records.
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.All(result.Value.Items, i => Assert.NotEqual("Not Allowed", i.Title));
    }

    // ── SearchEmployeeDocuments: out-of-range page ────────────────────────

    [Fact]
    public async Task SearchEmployeeDocuments_Out_Of_Range_Page_Returns_Empty_Items_With_Correct_TotalCount()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        await SeedDoc(db, companyId, type.Id, "Doc", "doc.pdf", Guid.NewGuid(), Now);

        var result = await SearchHandler(db).HandleAsync(
            new SearchEmployeeDocumentsRequest { CompanyId = companyId, PageNumber = 999, PageSize = 20 },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Empty(result.Value.Items);
    }

    // ── SearchEmployeeDocuments: validator ────────────────────────────────

    [Fact]
    public void SearchEmployeeDocuments_Validator_Rejects_Oversized_SearchText()
    {
        var result = new SearchEmployeeDocumentsValidator().Validate(new SearchEmployeeDocumentsRequest
        {
            CompanyId = Guid.NewGuid(),
            SearchText = new string('x', 201),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchEmployeeDocumentsRequest.SearchText));
    }

    // ── ListSharedCompanyDocuments: cross-company isolation ───────────────

    [Fact]
    public async Task ListSharedCompanyDocuments_TotalCount_Excludes_Other_Company_Records()
    {
        await using var db = BuildContext();
        var companyA  = Guid.NewGuid();
        var companyB  = Guid.NewGuid();
        var categoryA = await SeedSharedCategory(db, companyA);
        var categoryB = await SeedSharedCategory(db, companyB);

        db.SharedCompanyDocuments.AddRange(
            CreateSharedDoc(companyA, "A Policy", categoryA.Id, Guid.NewGuid(), Now),
            CreateSharedDoc(companyA, "B Policy", categoryA.Id, Guid.NewGuid(), Now),
            CreateSharedDoc(companyB, "C Policy", categoryB.Id, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await SharedHandler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Fact]
    public async Task ListSharedCompanyDocuments_Out_Of_Range_Page_Returns_Empty_Items_With_Correct_TotalCount()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedSharedCategory(db, companyId);

        db.SharedCompanyDocuments.Add(CreateSharedDoc(companyId, "Policy", category.Id, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await SharedHandler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, PageNumber = 999, PageSize = 20 },
            CancellationToken.None);

        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Empty(result.Value.Items);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static SearchEmployeeDocumentsHandler SearchHandler(DocumentsDbContext db, Dictionary<Guid, string>? names = null) =>
        new(db, new FakeEmployeeNameReader(names));

    private static ListSharedCompanyDocumentsHandler SharedHandler(DocumentsDbContext db) =>
        new(db, new FakeEmployeeNameReader());

    private static async Task<DocumentType> SeedType(DocumentsDbContext db, Guid companyId, string name = "General")
    {
        var dt = DocumentType.Create(Guid.NewGuid(), companyId, name, null, Now);
        db.DocumentTypes.Add(dt);
        await db.SaveChangesAsync();
        return dt;
    }

    private static async Task<(EmployeeDocument Ed, Document Doc)> SeedDoc(
        DocumentsDbContext db, Guid companyId, Guid typeId, string title, string fileName,
        Guid employeeId, DateTimeOffset createdAt)
    {
        var doc = Document.Create(Guid.NewGuid(), companyId, employeeId, title, null, typeId,
            fileName, 100, "application/pdf", $"key/{fileName}", null, Guid.NewGuid(), createdAt);
        var ed  = EmployeeDocument.Create(Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), createdAt);
        db.Documents.Add(doc);
        db.EmployeeDocuments.Add(ed);
        await db.SaveChangesAsync();
        return (ed, doc);
    }

    private static async Task<CompanyDocumentCategory> SeedSharedCategory(DocumentsDbContext db, Guid companyId, string name = "Policy")
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, name, Now);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static SharedCompanyDocument CreateSharedDoc(Guid companyId, string title, Guid categoryId, Guid createdBy, DateTimeOffset now) =>
        SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, title, null, categoryId,
            $"key/{Guid.NewGuid():N}.pdf", $"{Guid.NewGuid():N}.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, createdBy, now);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
