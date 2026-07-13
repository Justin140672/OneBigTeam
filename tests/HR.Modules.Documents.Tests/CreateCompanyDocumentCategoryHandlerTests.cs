using HR.Modules.Documents.Features.CreateCompanyDocumentCategory;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class CreateCompanyDocumentCategoryHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_Category()
    {
        await using var db = BuildContext();
        var handler   = new CreateCompanyDocumentCategoryHandler(db, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreateCompanyDocumentCategoryRequest { CompanyId = companyId, Name = "Policy" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal("Policy",  result.Value.Name);
        Assert.True(result.Value.IsActive);

        var saved = await db.CompanyDocumentCategories.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Active_Name_Already_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        await Handler(db).HandleAsync(
            new CreateCompanyDocumentCategoryRequest { CompanyId = companyId, Name = "Policy" },
            CancellationToken.None);

        var result = await Handler(db).HandleAsync(
            new CreateCompanyDocumentCategoryRequest { CompanyId = companyId, Name = "Policy" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Conflict_Check_Is_Case_Sensitive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        await Handler(db).HandleAsync(
            new CreateCompanyDocumentCategoryRequest { CompanyId = companyId, Name = "Policy" },
            CancellationToken.None);

        var result = await Handler(db).HandleAsync(
            new CreateCompanyDocumentCategoryRequest { CompanyId = companyId, Name = "policy" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Name_In_Different_Companies()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await Handler(db).HandleAsync(
            new CreateCompanyDocumentCategoryRequest { CompanyId = companyA, Name = "Policy" },
            CancellationToken.None);

        var result = await Handler(db).HandleAsync(
            new CreateCompanyDocumentCategoryRequest { CompanyId = companyB, Name = "Policy" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Allows_Reusing_Name_Of_A_Deactivated_Category()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var first = await Handler(db).HandleAsync(
            new CreateCompanyDocumentCategoryRequest { CompanyId = companyId, Name = "Policy" },
            CancellationToken.None);

        var toDeactivate = await db.CompanyDocumentCategories.SingleAsync(c => c.Id == first.Value!.Id);
        toDeactivate.Deactivate(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new CreateCompanyDocumentCategoryRequest { CompanyId = companyId, Name = "Policy" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Trims_Name()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new CreateCompanyDocumentCategoryRequest { CompanyId = Guid.NewGuid(), Name = "  Policy  " },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Policy", result.Value!.Name);
    }

    private static CreateCompanyDocumentCategoryHandler Handler(DocumentsDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
