using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ListRequiredDocumentsForPositionProfile;
using HR.Modules.Employees.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ListRequiredDocumentsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Active_RequiredDocuments_With_Names_For_Profile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, Now);
        context.PositionProfiles.Add(profile);

        var docTypeAId = Guid.NewGuid();
        var docTypeBId = Guid.NewGuid();

        var docA = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profile.Id, docTypeAId, true, 30, false, Guid.NewGuid(), Now);
        var docB = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profile.Id, docTypeBId, false, null, true, Guid.NewGuid(), Now);
        context.PositionProfileRequiredDocuments.AddRange(docA, docB);
        await context.SaveChangesAsync();

        var names = new Dictionary<Guid, string> { [docTypeAId] = "Passport", [docTypeBId] = "Right To Work" };
        var result = await BuildHandler(context, names).HandleAsync(
            new ListRequiredDocumentsRequest { CompanyId = companyId, PositionProfileId = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Contains(result.Value.Items, i => i.DocumentTypeId == docTypeAId && i.DocumentTypeName == "Passport");
        Assert.Contains(result.Value.Items, i => i.DocumentTypeId == docTypeBId && i.DocumentTypeName == "Right To Work");
    }

    [Fact]
    public async Task HandleAsync_Excludes_Inactive_RequiredDocuments()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, Now);
        context.PositionProfiles.Add(profile);

        var active = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profile.Id, Guid.NewGuid(), true, null, false, Guid.NewGuid(), Now);
        var inactive = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profile.Id, Guid.NewGuid(), true, null, false, Guid.NewGuid(), Now);
        inactive.Deactivate();

        context.PositionProfileRequiredDocuments.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ListRequiredDocumentsRequest { CompanyId = companyId, PositionProfileId = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(active.Id, result.Value.Items[0].Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_RequiredDocuments()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ListRequiredDocumentsRequest { CompanyId = companyId, PositionProfileId = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_PositionProfile()
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context).HandleAsync(
            new ListRequiredDocumentsRequest { CompanyId = Guid.NewGuid(), PositionProfileId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Isolates_Results_By_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyA, null, "Engineer", null, false, null, Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyB, null, "Engineer", null, false, null, Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var docForA = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyA, profileA.Id, Guid.NewGuid(), true, null, false, Guid.NewGuid(), Now);
        var docForB = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyB, profileB.Id, Guid.NewGuid(), true, null, false, Guid.NewGuid(), Now);
        context.PositionProfileRequiredDocuments.AddRange(docForA, docForB);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ListRequiredDocumentsRequest { CompanyId = companyA, PositionProfileId = profileA.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(docForA.Id, result.Value.Items[0].Id);
    }

    [Fact]
    public async Task HandleAsync_Isolates_Results_By_PositionProfile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Manager", null, true, null, Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var docForA = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profileA.Id, Guid.NewGuid(), true, null, false, Guid.NewGuid(), Now);
        var docForB = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profileB.Id, Guid.NewGuid(), true, null, false, Guid.NewGuid(), Now);
        context.PositionProfileRequiredDocuments.AddRange(docForA, docForB);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ListRequiredDocumentsRequest { CompanyId = companyId, PositionProfileId = profileA.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(docForA.Id, result.Value.Items[0].Id);
    }

    private static ListRequiredDocumentsHandler BuildHandler(
        EmployeesDbContext context,
        Dictionary<Guid, string>? names = null)
        => new(context, new StubDocumentTypeReader(names ?? []));

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }

    private sealed class StubDocumentTypeReader(Dictionary<Guid, string> names) : IDocumentTypeReader
    {
        public Task<bool> ExistsAsync(Guid companyId, Guid documentTypeId, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> documentTypeIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(names);
    }
}
