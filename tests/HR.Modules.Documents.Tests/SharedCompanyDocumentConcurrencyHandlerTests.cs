using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;
using HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAudience;
using HR.Modules.Documents.Features.UpdateSharedCompanyDocumentMetadata;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

// Ticket 2 (optimistic concurrency rollout): SharedCompanyDocument.Version coverage for the three
// slices that all mutate the same aggregate row — metadata, audience and acknowledgement-settings.
// Each has its own int? ExpectedVersion on the request. Two DbContext instances over one EF
// InMemory database: the winning context saves first (bumping the store's Version), then the
// handler under test saves against a fresh context with a stale ExpectedVersion, raising
// DbUpdateConcurrencyException exactly as real Postgres would. On the stale path the handler
// returns Error.Concurrency before publishing, so no audit event is emitted.
public class SharedCompanyDocumentConcurrencyHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<DocumentsDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<DocumentsDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task<(string DbName, Guid CompanyId, Guid CategoryId, Guid DocumentId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();

        await using var seed = new DocumentsDbContext(Options(dbName));
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", Now);
        seed.CompanyDocumentCategories.Add(category);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Title", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        seed.SharedCompanyDocuments.Add(doc);
        await seed.SaveChangesAsync();

        return (dbName, companyId, category.Id, doc.Id);
    }

    // ── Metadata ────────────────────────────────────────────────────────────────

    private static UpdateSharedCompanyDocumentMetadataHandler MetadataHandler(
        DocumentsDbContext db, FakeAuditPublisher audit) =>
        new(db, new FakeEmployeeAudienceReader(), audit, new FakeClock(FixedUtcNow));

    private static UpdateSharedCompanyDocumentMetadataRequest MetadataRequest(
        Guid companyId, Guid documentId, Guid categoryId, int? expectedVersion, string title) =>
        new()
        {
            CompanyId = companyId,
            DocumentId = documentId,
            Title = title,
            CategoryId = categoryId,
            ExpectedVersion = expectedVersion,
        };

    [Fact]
    public async Task Metadata_Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, categoryId, documentId) = await SeedAsync();

        await using var db = new DocumentsDbContext(Options(dbName));
        var result = await MetadataHandler(db, new FakeAuditPublisher()).HandleAsync(
            MetadataRequest(companyId, documentId, categoryId, expectedVersion: 1, title: "Renamed"),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);

        await using var verify = new DocumentsDbContext(Options(dbName));
        Assert.Equal(2, (await verify.SharedCompanyDocuments.SingleAsync()).Version);
    }

    [Fact]
    public async Task Metadata_Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, categoryId, documentId) = await SeedAsync();

        var audit = new FakeAuditPublisher();
        await using var db = new DocumentsDbContext(Options(dbName));
        var result = await MetadataHandler(db, audit).HandleAsync(
            MetadataRequest(companyId, documentId, categoryId, expectedVersion: null, title: "Second"),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new DocumentsDbContext(Options(dbName));
        var saved = await verify.SharedCompanyDocuments.SingleAsync();
        Assert.Equal("Title", saved.Title);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Metadata_Stale_ExpectedVersion_Returns_Concurrency_And_Publishes_No_Audit_Event()
    {
        var (dbName, companyId, categoryId, documentId) = await SeedAsync();

        await using (var winner = new DocumentsDbContext(Options(dbName)))
        {
            var winnerResult = await MetadataHandler(winner, new FakeAuditPublisher()).HandleAsync(
                MetadataRequest(companyId, documentId, categoryId, expectedVersion: 1, title: "Winner"),
                Guid.NewGuid(), CancellationToken.None);
            Assert.True(winnerResult.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        await using var loser = new DocumentsDbContext(Options(dbName));
        var result = await MetadataHandler(loser, audit).HandleAsync(
            MetadataRequest(companyId, documentId, categoryId, expectedVersion: 1, title: "Loser"),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new DocumentsDbContext(Options(dbName));
        var saved = await verify.SharedCompanyDocuments.SingleAsync();
        Assert.Equal("Winner", saved.Title);
        Assert.Equal(2, saved.Version);
    }

    // ── Audience ────────────────────────────────────────────────────────────────

    private static UpdateSharedCompanyDocumentAudienceHandler AudienceHandler(
        DocumentsDbContext db, FakeEmployeeAudienceReader reader, FakeAuditPublisher audit) =>
        new(db,
            new SharedCompanyDocumentAudienceRuleBuilder(reader),
            new SharedCompanyDocumentAudienceMatcher(db, reader),
            new SharedCompanyDocumentAudienceDescriber(reader, new FakeEmployeeNameReader()),
            audit,
            new FakeClock(FixedUtcNow));

    private static UpdateSharedCompanyDocumentAudienceRequest AudienceRequest(
        Guid companyId, Guid documentId, int? expectedVersion, Guid departmentId) =>
        new()
        {
            CompanyId = companyId,
            DocumentId = documentId,
            AudienceDepartmentIds = [departmentId],
            ExpectedVersion = expectedVersion,
        };

    [Fact]
    public async Task Audience_Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, _, documentId) = await SeedAsync();
        var departmentId = Guid.NewGuid();
        var reader = new FakeEmployeeAudienceReader();
        reader.ExistingDepartmentIds.Add(departmentId);

        await using var db = new DocumentsDbContext(Options(dbName));
        var result = await AudienceHandler(db, reader, new FakeAuditPublisher()).HandleAsync(
            AudienceRequest(companyId, documentId, expectedVersion: 1, departmentId),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);

        await using var verify = new DocumentsDbContext(Options(dbName));
        Assert.Equal(2, (await verify.SharedCompanyDocuments.SingleAsync()).Version);
    }

    [Fact]
    public async Task Audience_Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, _, documentId) = await SeedAsync();
        var deptB = Guid.NewGuid();
        var reader = new FakeEmployeeAudienceReader();
        reader.ExistingDepartmentIds.Add(deptB);

        var audit = new FakeAuditPublisher();
        await using var db = new DocumentsDbContext(Options(dbName));
        var result = await AudienceHandler(db, reader, audit).HandleAsync(
            AudienceRequest(companyId, documentId, expectedVersion: null, deptB),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new DocumentsDbContext(Options(dbName));
        Assert.Equal(1, (await verify.SharedCompanyDocuments.SingleAsync()).Version);
    }

    [Fact]
    public async Task Audience_Stale_ExpectedVersion_Returns_Concurrency_And_Publishes_No_Audit_Event()
    {
        var (dbName, companyId, _, documentId) = await SeedAsync();
        var deptA = Guid.NewGuid();
        var deptB = Guid.NewGuid();
        var reader = new FakeEmployeeAudienceReader();
        reader.ExistingDepartmentIds.Add(deptA);
        reader.ExistingDepartmentIds.Add(deptB);

        await using (var winner = new DocumentsDbContext(Options(dbName)))
        {
            var winnerResult = await AudienceHandler(winner, reader, new FakeAuditPublisher()).HandleAsync(
                AudienceRequest(companyId, documentId, expectedVersion: 1, deptA),
                Guid.NewGuid(), CancellationToken.None);
            Assert.True(winnerResult.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        await using var loser = new DocumentsDbContext(Options(dbName));
        var result = await AudienceHandler(loser, reader, audit).HandleAsync(
            AudienceRequest(companyId, documentId, expectedVersion: 1, deptB),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new DocumentsDbContext(Options(dbName));
        Assert.Equal(2, (await verify.SharedCompanyDocuments.SingleAsync()).Version);
    }

    // ── Acknowledgement settings ────────────────────────────────────────────────

    private static UpdateSharedCompanyDocumentAcknowledgementSettingsHandler AckHandler(
        DocumentsDbContext db, FakeAuditPublisher audit) =>
        new(db,
            new SharedCompanyDocumentAudienceMatcher(db, new FakeEmployeeAudienceReader()),
            new FakeTaskCanceller(),
            new FakeOpenTaskBySourceEntityReader(),
            new FakeNotificationWriter(),
            audit,
            new FakeClock(FixedUtcNow));

    private static UpdateSharedCompanyDocumentAcknowledgementSettingsRequest AckRequest(
        Guid companyId, Guid documentId, int? expectedVersion, DateOnly dueDate) =>
        new()
        {
            CompanyId = companyId,
            DocumentId = documentId,
            RequiresAcknowledgement = true,
            AcknowledgementDueDate = dueDate,
            ExpectedVersion = expectedVersion,
        };

    [Fact]
    public async Task AckSettings_Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, _, documentId) = await SeedAsync();

        await using var db = new DocumentsDbContext(Options(dbName));
        var result = await AckHandler(db, new FakeAuditPublisher()).HandleAsync(
            AckRequest(companyId, documentId, expectedVersion: 1, new DateOnly(2027, 1, 1)),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);

        await using var verify = new DocumentsDbContext(Options(dbName));
        Assert.Equal(2, (await verify.SharedCompanyDocuments.SingleAsync()).Version);
    }

    [Fact]
    public async Task AckSettings_Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, _, documentId) = await SeedAsync();

        var audit = new FakeAuditPublisher();
        await using var db = new DocumentsDbContext(Options(dbName));
        var result = await AckHandler(db, audit).HandleAsync(
            AckRequest(companyId, documentId, expectedVersion: null, new DateOnly(2027, 6, 1)),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new DocumentsDbContext(Options(dbName));
        var saved = await verify.SharedCompanyDocuments.SingleAsync();
        Assert.False(saved.RequiresAcknowledgement);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task AckSettings_Stale_ExpectedVersion_Returns_Concurrency_And_Publishes_No_Audit_Event()
    {
        var (dbName, companyId, _, documentId) = await SeedAsync();

        await using (var winner = new DocumentsDbContext(Options(dbName)))
        {
            var winnerResult = await AckHandler(winner, new FakeAuditPublisher()).HandleAsync(
                AckRequest(companyId, documentId, expectedVersion: 1, new DateOnly(2027, 1, 1)),
                Guid.NewGuid(), CancellationToken.None);
            Assert.True(winnerResult.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        await using var loser = new DocumentsDbContext(Options(dbName));
        var result = await AckHandler(loser, audit).HandleAsync(
            AckRequest(companyId, documentId, expectedVersion: 1, new DateOnly(2027, 9, 1)),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new DocumentsDbContext(Options(dbName));
        Assert.Equal(2, (await verify.SharedCompanyDocuments.SingleAsync()).Version);
    }
}
