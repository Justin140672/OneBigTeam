using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UpdateDocumentType;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class UpdateDocumentTypeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Updates_Name_And_Description()
    {
        await using var db = BuildContext();
        var (companyId, typeId) = await SeedDocumentType(db, "Contract", null);

        var result = await Handler(db).HandleAsync(
            new UpdateDocumentTypeRequest
            {
                CompanyId      = companyId,
                DocumentTypeId = typeId,
                Name           = "Employment Contract",
                Description    = "Updated description"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Employment Contract", result.Value!.Name);
        Assert.Equal("Updated description", result.Value.Description);
        Assert.True(result.Value.IsActive);

        var saved = await db.DocumentTypes.SingleAsync();
        Assert.Equal("Employment Contract", saved.Name);
    }

    [Fact]
    public async Task HandleAsync_Clears_Description_When_Null()
    {
        await using var db = BuildContext();
        var (companyId, typeId) = await SeedDocumentType(db, "Contract", "Old description");

        var result = await Handler(db).HandleAsync(
            new UpdateDocumentTypeRequest
            {
                CompanyId      = companyId,
                DocumentTypeId = typeId,
                Name           = "Contract",
                Description    = null
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Description);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Name_On_Self()
    {
        await using var db = BuildContext();
        var (companyId, typeId) = await SeedDocumentType(db, "Contract", null);

        var result = await Handler(db).HandleAsync(
            new UpdateDocumentTypeRequest
            {
                CompanyId      = companyId,
                DocumentTypeId = typeId,
                Name           = "Contract",
                Description    = "Now with a description"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Name_Taken_By_Another_Type()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now       = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        db.DocumentTypes.Add(DocumentType.Create(Guid.NewGuid(), companyId, "Passport",  null, now));
        var target = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, now);
        db.DocumentTypes.Add(target);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateDocumentTypeRequest
            {
                CompanyId      = companyId,
                DocumentTypeId = target.Id,
                Name           = "Passport"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DocumentType_Does_Not_Exist()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new UpdateDocumentTypeRequest
            {
                CompanyId      = Guid.NewGuid(),
                DocumentTypeId = Guid.NewGuid(),
                Name           = "Contract"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DocumentType_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var (_, typeId) = await SeedDocumentType(db, "Contract", null);

        var result = await Handler(db).HandleAsync(
            new UpdateDocumentTypeRequest
            {
                CompanyId      = Guid.NewGuid(), // different company
                DocumentTypeId = typeId,
                Name           = "Contract"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DocumentType_Is_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now       = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var inactive = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, now);
        inactive.Deactivate(now);
        db.DocumentTypes.Add(inactive);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateDocumentTypeRequest
            {
                CompanyId      = companyId,
                DocumentTypeId = inactive.Id,
                Name           = "Contract"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Trims_Name_And_Description()
    {
        await using var db = BuildContext();
        var (companyId, typeId) = await SeedDocumentType(db, "Contract", null);

        var result = await Handler(db).HandleAsync(
            new UpdateDocumentTypeRequest
            {
                CompanyId      = companyId,
                DocumentTypeId = typeId,
                Name           = "  Passport  ",
                Description    = "  Some description  "
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Passport",         result.Value!.Name);
        Assert.Equal("Some description", result.Value.Description);
    }

    [Fact]
    public async Task HandleAsync_Sets_Description_Null_When_Whitespace_Only()
    {
        await using var db = BuildContext();
        var (companyId, typeId) = await SeedDocumentType(db, "Contract", "Old description");

        var result = await Handler(db).HandleAsync(
            new UpdateDocumentTypeRequest
            {
                CompanyId      = companyId,
                DocumentTypeId = typeId,
                Name           = "Contract",
                Description    = "   "
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Description);
    }

    private static UpdateDocumentTypeHandler Handler(DocumentsDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static async Task<(Guid CompanyId, Guid TypeId)> SeedDocumentType(
        DocumentsDbContext db, string name, string? description)
    {
        var companyId = Guid.NewGuid();
        var now       = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var type      = DocumentType.Create(Guid.NewGuid(), companyId, name, description, now);
        db.DocumentTypes.Add(type);
        await db.SaveChangesAsync();
        return (companyId, type.Id);
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
