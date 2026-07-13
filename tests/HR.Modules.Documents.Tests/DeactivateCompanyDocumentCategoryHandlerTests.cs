using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.DeactivateCompanyDocumentCategory;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class DeactivateCompanyDocumentCategoryHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Deactivates_Category()
    {
        await using var db = BuildContext();
        var (companyId, categoryId) = await Seed(db, "Policy");

        var result = await Handler(db).HandleAsync(
            new DeactivateCompanyDocumentCategoryRequest { CompanyId = companyId, CategoryId = categoryId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.CompanyDocumentCategories.SingleAsync();
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new DeactivateCompanyDocumentCategoryRequest { CompanyId = Guid.NewGuid(), CategoryId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var (_, categoryId) = await Seed(db, "Policy");

        var result = await Handler(db).HandleAsync(
            new DeactivateCompanyDocumentCategoryRequest { CompanyId = Guid.NewGuid(), CategoryId = categoryId },
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

        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", now);
        category.Deactivate(now);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new DeactivateCompanyDocumentCategoryRequest { CompanyId = companyId, CategoryId = category.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static DeactivateCompanyDocumentCategoryHandler Handler(DocumentsDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static async Task<(Guid CompanyId, Guid CategoryId)> Seed(DocumentsDbContext db, string name)
    {
        var companyId = Guid.NewGuid();
        var category  = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, name, new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return (companyId, category.Id);
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
