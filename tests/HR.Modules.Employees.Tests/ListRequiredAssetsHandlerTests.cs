using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ListRequiredAssetsForPositionProfile;
using HR.Modules.Employees.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ListRequiredAssetsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Active_RequiredAssets_With_Names_For_Profile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var categoryAId = Guid.NewGuid();
        var categoryBId = Guid.NewGuid();

        var assetA = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyId, profile.Id, categoryAId, true, 1, Guid.NewGuid(), Now);
        var assetB = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyId, profile.Id, categoryBId, false, 2, Guid.NewGuid(), Now);
        context.PositionProfileRequiredAssets.AddRange(assetA, assetB);
        await context.SaveChangesAsync();

        var names = new Dictionary<Guid, string> { [categoryAId] = "Laptop", [categoryBId] = "Monitor" };
        var result = await BuildHandler(context, names).HandleAsync(
            new ListRequiredAssetsRequest { CompanyId = companyId, PositionProfileId = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Contains(result.Value.Items, i => i.AssetCategoryId == categoryAId && i.AssetCategoryName == "Laptop");
        Assert.Contains(result.Value.Items, i => i.AssetCategoryId == categoryBId && i.AssetCategoryName == "Monitor");
    }

    [Fact]
    public async Task HandleAsync_Excludes_Inactive_RequiredAssets()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var active = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyId, profile.Id, Guid.NewGuid(), true, 1, Guid.NewGuid(), Now);
        var inactive = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyId, profile.Id, Guid.NewGuid(), true, 1, Guid.NewGuid(), Now);
        inactive.Deactivate();

        context.PositionProfileRequiredAssets.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ListRequiredAssetsRequest { CompanyId = companyId, PositionProfileId = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(active.Id, result.Value.Items[0].Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_RequiredAssets()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ListRequiredAssetsRequest { CompanyId = companyId, PositionProfileId = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_PositionProfile()
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context).HandleAsync(
            new ListRequiredAssetsRequest { CompanyId = Guid.NewGuid(), PositionProfileId = Guid.NewGuid() },
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

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyA, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyB, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var assetForA = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyA, profileA.Id, Guid.NewGuid(), true, 1, Guid.NewGuid(), Now);
        var assetForB = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyB, profileB.Id, Guid.NewGuid(), true, 1, Guid.NewGuid(), Now);
        context.PositionProfileRequiredAssets.AddRange(assetForA, assetForB);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ListRequiredAssetsRequest { CompanyId = companyA, PositionProfileId = profileA.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(assetForA.Id, result.Value.Items[0].Id);
    }

    [Fact]
    public async Task HandleAsync_Isolates_Results_By_PositionProfile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Manager", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var assetForA = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyId, profileA.Id, Guid.NewGuid(), true, 1, Guid.NewGuid(), Now);
        var assetForB = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyId, profileB.Id, Guid.NewGuid(), true, 1, Guid.NewGuid(), Now);
        context.PositionProfileRequiredAssets.AddRange(assetForA, assetForB);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ListRequiredAssetsRequest { CompanyId = companyId, PositionProfileId = profileA.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(assetForA.Id, result.Value.Items[0].Id);
    }

    private static ListRequiredAssetsHandler BuildHandler(
        EmployeesDbContext context,
        Dictionary<Guid, string>? names = null)
        => new(context, new StubAssetCategoryReader(names ?? []));

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }

    private sealed class StubAssetCategoryReader(Dictionary<Guid, string> names) : IAssetCategoryReader
    {
        public Task<bool> ExistsAsync(Guid companyId, Guid assetCategoryId, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> assetCategoryIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(names);
    }
}
