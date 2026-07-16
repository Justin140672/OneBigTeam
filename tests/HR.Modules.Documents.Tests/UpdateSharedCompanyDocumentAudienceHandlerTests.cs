using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAudience;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class UpdateSharedCompanyDocumentAudienceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeEmployeeNameReader(Dictionary<Guid, string>? names = null) : IEmployeeNameReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(names ?? new Dictionary<Guid, string>());
    }

    [Fact]
    public async Task HandleAsync_Replaces_All_Employees_Audience_With_Department_Scoped_Audience()
    {
        await using var db = BuildContext();
        var companyId    = Guid.NewGuid();
        var category     = await SeedCategory(db, companyId);
        var departmentId = Guid.NewGuid();
        var updatedBy    = Guid.NewGuid();

        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.ExistingDepartmentIds.Add(departmentId);
        audienceReader.DepartmentNames[departmentId] = "Engineering";

        var result = await Handler(db, audienceReader).HandleAsync(
            new UpdateSharedCompanyDocumentAudienceRequest
            {
                CompanyId             = companyId,
                DocumentId            = doc.Id,
                AudienceDepartmentIds = [departmentId],
            },
            updatedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([departmentId], result.Value!.AudienceDepartmentIds);
        Assert.Equal("Departments: Engineering", result.Value.AudienceDescription);

        var savedRules = await db.SharedCompanyDocumentAudienceRules.ToListAsync();
        Assert.Single(savedRules);
        Assert.Equal(SharedCompanyDocumentAudienceRuleType.Department, savedRules[0].RuleType);
        Assert.Equal(departmentId, savedRules[0].TargetId);
    }

    [Fact]
    public async Task HandleAsync_Replaces_Existing_Rules_Rather_Than_Appending()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var oldDeptId  = Guid.NewGuid();
        var newDeptId  = Guid.NewGuid();

        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, oldDeptId));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.ExistingDepartmentIds.Add(newDeptId);

        await Handler(db, audienceReader).HandleAsync(
            new UpdateSharedCompanyDocumentAudienceRequest
            {
                CompanyId             = companyId,
                DocumentId            = doc.Id,
                AudienceDepartmentIds = [newDeptId],
            },
            Guid.NewGuid(), CancellationToken.None);

        var savedRules = await db.SharedCompanyDocumentAudienceRules.ToListAsync();
        Assert.Single(savedRules);
        Assert.Equal(newDeptId, savedRules[0].TargetId);
    }

    [Fact]
    public async Task HandleAsync_Clearing_Audience_Rules_Reverts_To_All_Employees()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentAudienceRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("All Employees", result.Value!.AudienceDescription);
        Assert.Empty(await db.SharedCompanyDocumentAudienceRules.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Document()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentAudienceRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentAudienceRequest
            {
                CompanyId             = companyId,
                DocumentId            = doc.Id,
                AudienceDepartmentIds = [Guid.NewGuid()],
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);

        // A failed validation must not leave a half-applied rule set behind.
        Assert.Empty(await db.SharedCompanyDocumentAudienceRules.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_When_Audience_Changes()
    {
        await using var db = BuildContext();
        var companyId    = Guid.NewGuid();
        var category     = await SeedCategory(db, companyId);
        var departmentId = Guid.NewGuid();
        var updatedBy    = Guid.NewGuid();

        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.ExistingDepartmentIds.Add(departmentId);
        var audit = new FakeAuditPublisher();

        await Handler(db, audienceReader, audit).HandleAsync(
            new UpdateSharedCompanyDocumentAudienceRequest
            {
                CompanyId             = companyId,
                DocumentId            = doc.Id,
                AudienceDepartmentIds = [departmentId],
            },
            updatedBy, CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("shared_company_document.audience_updated", evt.EventType);
        Assert.Equal(doc.Id,                                     evt.EntityId);
        Assert.Equal(updatedBy,                                  evt.ActorUserId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Audience_Unchanged()
    {
        await using var db = BuildContext();
        var companyId    = Guid.NewGuid();
        var category     = await SeedCategory(db, companyId);
        var departmentId = Guid.NewGuid();

        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, departmentId));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.ExistingDepartmentIds.Add(departmentId);
        var audit = new FakeAuditPublisher();

        await Handler(db, audienceReader, audit).HandleAsync(
            new UpdateSharedCompanyDocumentAudienceRequest
            {
                CompanyId             = companyId,
                DocumentId            = doc.Id,
                AudienceDepartmentIds = [departmentId], // same set, resubmitted
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    private static UpdateSharedCompanyDocumentAudienceHandler Handler(
        DocumentsDbContext db, FakeEmployeeAudienceReader? audienceReader = null, FakeAuditPublisher? auditPublisher = null)
    {
        var reader = audienceReader ?? new FakeEmployeeAudienceReader();
        var nameReader = new FakeEmployeeNameReader();
        return new UpdateSharedCompanyDocumentAudienceHandler(
            db,
            new SharedCompanyDocumentAudienceRuleBuilder(reader),
            new SharedCompanyDocumentAudienceMatcher(db, reader),
            new SharedCompanyDocumentAudienceDescriber(reader, nameReader),
            auditPublisher ?? new FakeAuditPublisher(),
            new FakeClock(FixedUtcNow));
    }

    private static SharedCompanyDocument CreateDoc(Guid companyId, Guid categoryId, Guid createdBy) =>
        SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, categoryId, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, createdBy, Now);

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
