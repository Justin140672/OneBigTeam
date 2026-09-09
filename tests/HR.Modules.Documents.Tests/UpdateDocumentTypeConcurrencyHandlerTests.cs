using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UpdateDocumentType;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

// Ticket 2 (optimistic concurrency rollout): DocumentType.Version coverage for UpdateDocumentType.
// Same two-context-over-one-InMemory-database approach as the Asset/Category concurrency tests.
// This slice publishes no audit/integration events, so the stale path only asserts nothing was
// committed.
public class UpdateDocumentTypeConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<DocumentsDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<DocumentsDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task<(string DbName, Guid CompanyId, Guid TypeId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();

        await using var seed = new DocumentsDbContext(Options(dbName));
        var type = DocumentType.Create(
            Guid.NewGuid(), companyId, "Contract", null, new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        seed.DocumentTypes.Add(type);
        await seed.SaveChangesAsync();

        return (dbName, companyId, type.Id);
    }

    private static UpdateDocumentTypeRequest Request(
        Guid companyId, Guid typeId, int? expectedVersion, string name) =>
        new()
        {
            CompanyId = companyId,
            DocumentTypeId = typeId,
            Name = name,
            ExpectedVersion = expectedVersion,
        };

    private static UpdateDocumentTypeHandler Handler(DocumentsDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, typeId) = await SeedAsync();

        await using var db = new DocumentsDbContext(Options(dbName));
        var result = await Handler(db).HandleAsync(
            Request(companyId, typeId, expectedVersion: 1, name: "Employment Contract"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
        Assert.Equal("Employment Contract", result.Value.Name);

        await using var verify = new DocumentsDbContext(Options(dbName));
        Assert.Equal(2, (await verify.DocumentTypes.SingleAsync()).Version);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, typeId) = await SeedAsync();

        await using var db = new DocumentsDbContext(Options(dbName));
        var result = await Handler(db).HandleAsync(
            Request(companyId, typeId, expectedVersion: null, name: "Second"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new DocumentsDbContext(Options(dbName));
        var saved = await verify.DocumentTypes.SingleAsync();
        Assert.Equal("Contract", saved.Name);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Leaves_Row_Unchanged()
    {
        var (dbName, companyId, typeId) = await SeedAsync();

        await using (var winner = new DocumentsDbContext(Options(dbName)))
        {
            var winnerResult = await Handler(winner).HandleAsync(
                Request(companyId, typeId, expectedVersion: 1, name: "Winner"), CancellationToken.None);
            Assert.True(winnerResult.IsSuccess);
        }

        await using var loser = new DocumentsDbContext(Options(dbName));
        var result = await Handler(loser).HandleAsync(
            Request(companyId, typeId, expectedVersion: 1, name: "Loser"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new DocumentsDbContext(Options(dbName));
        var saved = await verify.DocumentTypes.SingleAsync();
        Assert.Equal("Winner", saved.Name);
        Assert.Equal(2, saved.Version);
    }
}
