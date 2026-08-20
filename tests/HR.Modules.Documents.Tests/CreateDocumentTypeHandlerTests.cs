using HR.Modules.Documents.Features.CreateDocumentType;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class CreateDocumentTypeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_DocumentType()
    {
        await using var db = BuildContext();
        var handler   = new CreateDocumentTypeHandler(db, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreateDocumentTypeRequest { CompanyId = companyId, Name = "Contract" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId,  result.Value!.CompanyId);
        Assert.Equal("Contract", result.Value.Name);
        Assert.Null(result.Value.Description);
        Assert.True(result.Value.IsActive);

        var saved = await db.DocumentTypes.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Creates_DocumentType_With_Description()
    {
        await using var db = BuildContext();
        var handler   = new CreateDocumentTypeHandler(db, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreateDocumentTypeRequest
            {
                CompanyId   = companyId,
                Name        = "Certificate",
                Description = "Training and professional certificates"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Training and professional certificates", result.Value!.Description);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Active_Name_Already_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        await handler(db).HandleAsync(
            new CreateDocumentTypeRequest { CompanyId = companyId, Name = "Contract" },
            CancellationToken.None);

        var result = await handler(db).HandleAsync(
            new CreateDocumentTypeRequest { CompanyId = companyId, Name = "Contract" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Conflict_Check_Is_Case_Insensitive()
    {
        // Case-insensitive uniqueness is deliberate (see backlog item on preventing
        // case-insensitive duplicate names across all "Name must be unique per company" entities)
        // — "Contract" and "contract" are the same document type name.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        await handler(db).HandleAsync(
            new CreateDocumentTypeRequest { CompanyId = companyId, Name = "Contract" },
            CancellationToken.None);

        var result = await handler(db).HandleAsync(
            new CreateDocumentTypeRequest { CompanyId = companyId, Name = "contract" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Name_In_Different_Companies()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await handler(db).HandleAsync(
            new CreateDocumentTypeRequest { CompanyId = companyA, Name = "Contract" },
            CancellationToken.None);

        var result = await handler(db).HandleAsync(
            new CreateDocumentTypeRequest { CompanyId = companyB, Name = "Contract" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Trims_Name_And_Description()
    {
        await using var db = BuildContext();
        var result = await handler(db).HandleAsync(
            new CreateDocumentTypeRequest
            {
                CompanyId   = Guid.NewGuid(),
                Name        = "  Contract  ",
                Description = "  Some description  "
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Contract",         result.Value!.Name);
        Assert.Equal("Some description", result.Value.Description);
    }

    [Fact]
    public async Task HandleAsync_Sets_Description_Null_When_Whitespace_Only()
    {
        await using var db = BuildContext();
        var result = await handler(db).HandleAsync(
            new CreateDocumentTypeRequest
            {
                CompanyId   = Guid.NewGuid(),
                Name        = "Contract",
                Description = "   "
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Description);
    }

    private static CreateDocumentTypeHandler handler(DocumentsDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
