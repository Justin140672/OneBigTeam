using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetSharedCompanyDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetSharedCompanyDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeEmployeeNameReader(Dictionary<Guid, string>? names = null) : IEmployeeNameReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(names ?? new Dictionary<Guid, string>());
    }

    [Fact]
    public async Task HandleAsync_Returns_Full_Metadata_And_Category_Name()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId, "Policy");
        var createdBy = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Remote Working Policy", "A description", category.Id,
            "key/p.pdf", "p.pdf", 500, "application/pdf",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), null, null, false, createdBy, Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var names = new Dictionary<Guid, string> { [createdBy] = "Laura Bennett" };
        var result = await Handler(db, names: names).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Remote Working Policy", result.Value!.Title);
        Assert.Equal("Policy",                result.Value.CategoryName);
        Assert.Equal(1,                       result.Value.VersionNumber);
        Assert.Equal("Draft",                 result.Value.Status);
        Assert.Equal("Laura Bennett",         result.Value.CreatedByName);
        Assert.Equal("Laura Bennett",         result.Value.UpdatedByName);
        Assert.Equal("All Employees",         result.Value.AudienceDescription);
        Assert.False(result.Value.RequiresAcknowledgement);
        Assert.Null(result.Value.AcknowledgementProgress);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Document()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyA  = Guid.NewGuid();
        var category  = await SeedCategory(db, companyA);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyA, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, null, null, false, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Version_History_Ordered_Newest_First()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, null, null, false, Guid.NewGuid(), Now);
        doc.ReplaceFile("key/v2.pdf", "v2.pdf", 200, "application/pdf", Guid.NewGuid(), Now.AddDays(1));
        db.SharedCompanyDocuments.Add(doc);

        db.SharedCompanyDocumentVersions.AddRange(
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 1, "key/v1.pdf", "v1.pdf", 100, "application/pdf", Guid.NewGuid(), Now),
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 2, "key/v2.pdf", "v2.pdf", 200, "application/pdf", Guid.NewGuid(), Now.AddDays(1)));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.Equal(2, result.Value!.VersionHistory.Count);
        Assert.Equal(2, result.Value.VersionHistory[0].VersionNumber);
        Assert.Equal(1, result.Value.VersionHistory[1].VersionNumber);
    }

    [Fact]
    public async Task HandleAsync_Describes_Department_Audience()
    {
        await using var db = BuildContext();
        var companyId    = Guid.NewGuid();
        var category     = await SeedCategory(db, companyId);
        var departmentId = Guid.NewGuid();
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, departmentId, null, false, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.DepartmentNames[departmentId] = "Engineering";

        var result = await Handler(db, audienceReader).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.Equal("Department: Engineering", result.Value!.AudienceDescription);
    }

    [Fact]
    public async Task HandleAsync_Returns_Acknowledgement_Progress_When_Required()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();
        var emp3 = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, null, null, requiresAcknowledgement: true, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);

        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(Guid.NewGuid(), companyId, doc.Id, emp1, 1, Now));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [emp1, emp2, emp3] };
        var names = new Dictionary<Guid, string> { [emp1] = "Tom Williams" };

        var result = await Handler(db, audienceReader, names).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.NotNull(result.Value!.AcknowledgementProgress);
        Assert.Equal(1, result.Value.AcknowledgementProgress!.AcknowledgedCount);
        Assert.Equal(3, result.Value.AcknowledgementProgress.EligibleCount);
        Assert.Equal(["Tom Williams"], result.Value.AcknowledgementProgress.AcknowledgedEmployeeNames);
    }

    [Fact]
    public async Task HandleAsync_Acknowledgement_Progress_Excludes_Old_Version_Acknowledgements()
    {
        // An employee acknowledged version 1; the document has since been replaced with version
        // 2 — that acknowledgement must not count towards the current version's progress.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var emp1 = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, null, null, requiresAcknowledgement: true, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        doc.ReplaceFile("key/v2.pdf", "v2.pdf", 200, "application/pdf", Guid.NewGuid(), Now.AddDays(1));
        db.SharedCompanyDocuments.Add(doc);

        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(Guid.NewGuid(), companyId, doc.Id, emp1, 1, Now));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [emp1] };

        var result = await Handler(db, audienceReader).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.Equal(0, result.Value!.AcknowledgementProgress!.AcknowledgedCount);
    }

    private static GetSharedCompanyDocumentHandler Handler(
        DocumentsDbContext db, FakeEmployeeAudienceReader? audienceReader = null, Dictionary<Guid, string>? names = null) =>
        new(db, new FakeEmployeeNameReader(names), audienceReader ?? new FakeEmployeeAudienceReader());

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
