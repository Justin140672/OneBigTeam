using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.DeactivateDocumentType;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class DeactivateDocumentTypeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Deactivates_DocumentType()
    {
        await using var db = BuildContext();
        var (companyId, typeId) = await Seed(db, "Contract");

        var result = await Handler(db).HandleAsync(
            new DeactivateDocumentTypeRequest { CompanyId = companyId, DocumentTypeId = typeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.DocumentTypes.SingleAsync();
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Sets_UpdatedAt()
    {
        await using var db = BuildContext();
        var (companyId, typeId) = await Seed(db, "Contract");

        await Handler(db).HandleAsync(
            new DeactivateDocumentTypeRequest { CompanyId = companyId, DocumentTypeId = typeId },
            CancellationToken.None);

        var saved = await db.DocumentTypes.SingleAsync();
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), saved.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DocumentType_Does_Not_Exist()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new DeactivateDocumentTypeRequest { CompanyId = Guid.NewGuid(), DocumentTypeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DocumentType_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var (_, typeId) = await Seed(db, "Contract");

        var result = await Handler(db).HandleAsync(
            new DeactivateDocumentTypeRequest { CompanyId = Guid.NewGuid(), DocumentTypeId = typeId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Already_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now       = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var type = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, now);
        type.Deactivate(now);
        db.DocumentTypes.Add(type);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new DeactivateDocumentTypeRequest { CompanyId = companyId, DocumentTypeId = type.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static DeactivateDocumentTypeHandler Handler(DocumentsDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static async Task<(Guid CompanyId, Guid TypeId)> Seed(DocumentsDbContext db, string name)
    {
        var companyId = Guid.NewGuid();
        var type      = DocumentType.Create(Guid.NewGuid(), companyId, name, null, new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        db.DocumentTypes.Add(type);
        await db.SaveChangesAsync();
        return (companyId, type.Id);
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
