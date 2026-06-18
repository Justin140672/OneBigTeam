using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ListDocumentTypes;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ListDocumentTypesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Active_Types_For_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.DocumentTypes.AddRange(
            DocumentType.Create(Guid.NewGuid(), companyId, "Passport",  null, Now),
            DocumentType.Create(Guid.NewGuid(), companyId, "Contract",  null, Now));
        await db.SaveChangesAsync();

        var result = await new ListDocumentTypesHandler(db).HandleAsync(
            new ListDocumentTypesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Returns_Items_Ordered_By_Name()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.DocumentTypes.AddRange(
            DocumentType.Create(Guid.NewGuid(), companyId, "Right To Work",  null, Now),
            DocumentType.Create(Guid.NewGuid(), companyId, "Contract",       null, Now),
            DocumentType.Create(Guid.NewGuid(), companyId, "Passport",       null, Now));
        await db.SaveChangesAsync();

        var result = await new ListDocumentTypesHandler(db).HandleAsync(
            new ListDocumentTypesRequest { CompanyId = companyId },
            CancellationToken.None);

        var names = result.Value!.Items.Select(i => i.Name).ToList();
        Assert.Equal(["Contract", "Passport", "Right To Work"], names);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Inactive_Types_By_Default()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var inactive = DocumentType.Create(Guid.NewGuid(), companyId, "Old Type", null, Now);
        inactive.Deactivate(Now);
        db.DocumentTypes.AddRange(
            DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, Now),
            inactive);
        await db.SaveChangesAsync();

        var result = await new ListDocumentTypesHandler(db).HandleAsync(
            new ListDocumentTypesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Contract", result.Value.Items[0].Name);
    }

    [Fact]
    public async Task HandleAsync_Includes_Inactive_Types_When_Requested()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var inactive = DocumentType.Create(Guid.NewGuid(), companyId, "Old Type", null, Now);
        inactive.Deactivate(Now);
        db.DocumentTypes.AddRange(
            DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, Now),
            inactive);
        await db.SaveChangesAsync();

        var result = await new ListDocumentTypesHandler(db).HandleAsync(
            new ListDocumentTypesRequest { CompanyId = companyId, IncludeInactive = true },
            CancellationToken.None);

        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_Company_Has_No_Types()
    {
        await using var db = BuildContext();

        var result = await new ListDocumentTypesHandler(db).HandleAsync(
            new ListDocumentTypesRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Types_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        db.DocumentTypes.AddRange(
            DocumentType.Create(Guid.NewGuid(), companyA, "Contract", null, Now),
            DocumentType.Create(Guid.NewGuid(), companyB, "Contract", null, Now));
        await db.SaveChangesAsync();

        var result = await new ListDocumentTypesHandler(db).HandleAsync(
            new ListDocumentTypesRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Maps_Description_Correctly()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.DocumentTypes.Add(
            DocumentType.Create(Guid.NewGuid(), companyId, "Certificate", "Training certs", Now));
        await db.SaveChangesAsync();

        var result = await new ListDocumentTypesHandler(db).HandleAsync(
            new ListDocumentTypesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal("Training certs", result.Value!.Items[0].Description);
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
