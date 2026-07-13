using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UpdateCompanyDocumentCategory;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class UpdateCompanyDocumentCategoryHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Renames_Category()
    {
        await using var db = BuildContext();
        var (companyId, categoryId) = await Seed(db, "Policy");

        var result = await Handler(db).HandleAsync(
            new UpdateCompanyDocumentCategoryRequest
            {
                CompanyId  = companyId,
                CategoryId = categoryId,
                Name       = "Handbook",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Handbook", result.Value!.Name);
        Assert.True(result.Value.IsActive);

        var saved = await db.CompanyDocumentCategories.SingleAsync();
        Assert.Equal("Handbook", saved.Name);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Name_On_Self()
    {
        await using var db = BuildContext();
        var (companyId, categoryId) = await Seed(db, "Policy");

        var result = await Handler(db).HandleAsync(
            new UpdateCompanyDocumentCategoryRequest
            {
                CompanyId  = companyId,
                CategoryId = categoryId,
                Name       = "Policy",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Name_Taken_By_Another_Category()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now       = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        db.CompanyDocumentCategories.Add(CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Handbook", now));
        var target = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", now);
        db.CompanyDocumentCategories.Add(target);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateCompanyDocumentCategoryRequest
            {
                CompanyId  = companyId,
                CategoryId = target.Id,
                Name       = "Handbook",
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new UpdateCompanyDocumentCategoryRequest
            {
                CompanyId  = Guid.NewGuid(),
                CategoryId = Guid.NewGuid(),
                Name       = "Policy",
            },
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
            new UpdateCompanyDocumentCategoryRequest
            {
                CompanyId  = Guid.NewGuid(), // different company
                CategoryId = categoryId,
                Name       = "Handbook",
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Is_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now       = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var inactive = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", now);
        inactive.Deactivate(now);
        db.CompanyDocumentCategories.Add(inactive);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateCompanyDocumentCategoryRequest
            {
                CompanyId  = companyId,
                CategoryId = inactive.Id,
                Name       = "Handbook",
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Trims_Name()
    {
        await using var db = BuildContext();
        var (companyId, categoryId) = await Seed(db, "Policy");

        var result = await Handler(db).HandleAsync(
            new UpdateCompanyDocumentCategoryRequest
            {
                CompanyId  = companyId,
                CategoryId = categoryId,
                Name       = "  Handbook  ",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Handbook", result.Value!.Name);
    }

    private static UpdateCompanyDocumentCategoryHandler Handler(DocumentsDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static async Task<(Guid CompanyId, Guid CategoryId)> Seed(DocumentsDbContext db, string name)
    {
        var companyId = Guid.NewGuid();
        var now       = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var category  = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, name, now);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return (companyId, category.Id);
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
