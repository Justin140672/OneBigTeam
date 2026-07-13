using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ListCompanyDocumentCategories;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ListCompanyDocumentCategoriesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Items_Ordered_By_Name()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.CompanyDocumentCategories.AddRange(
            CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Procedure", Now),
            CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Form",      Now),
            CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Handbook",  Now));
        await db.SaveChangesAsync();

        var result = await new ListCompanyDocumentCategoriesHandler(db).HandleAsync(
            new ListCompanyDocumentCategoriesRequest { CompanyId = companyId },
            CancellationToken.None);

        var names = result.Value!.Items.Select(i => i.Name).ToList();
        Assert.Equal(["Form", "Handbook", "Procedure"], names);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Inactive_Categories_By_Default()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var inactive = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Old", Now);
        inactive.Deactivate(Now);
        db.CompanyDocumentCategories.AddRange(
            CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", Now),
            inactive);
        await db.SaveChangesAsync();

        var result = await new ListCompanyDocumentCategoriesHandler(db).HandleAsync(
            new ListCompanyDocumentCategoriesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Policy", result.Value.Items[0].Name);
    }

    [Fact]
    public async Task HandleAsync_Includes_Inactive_Categories_When_Requested()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var inactive = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Old", Now);
        inactive.Deactivate(Now);
        db.CompanyDocumentCategories.AddRange(
            CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", Now),
            inactive);
        await db.SaveChangesAsync();

        var result = await new ListCompanyDocumentCategoriesHandler(db).HandleAsync(
            new ListCompanyDocumentCategoriesRequest { CompanyId = companyId, IncludeInactive = true },
            CancellationToken.None);

        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Categories_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        db.CompanyDocumentCategories.AddRange(
            CompanyDocumentCategory.Create(Guid.NewGuid(), companyA, "Policy", Now),
            CompanyDocumentCategory.Create(Guid.NewGuid(), companyB, "Policy", Now));
        await db.SaveChangesAsync();

        var result = await new ListCompanyDocumentCategoriesHandler(db).HandleAsync(
            new ListCompanyDocumentCategoriesRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
