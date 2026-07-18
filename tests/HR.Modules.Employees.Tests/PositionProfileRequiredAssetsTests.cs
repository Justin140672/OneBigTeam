using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetPositionProfile;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class PositionProfileRequiredAssetsTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_ActiveRequiredAssets_With_Profile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);

        var active = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyId, profile.Id, Guid.NewGuid(), true, 2, Guid.NewGuid(), Now);
        var inactive = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyId, profile.Id, Guid.NewGuid(), false, 1, Guid.NewGuid(), Now);
        inactive.Deactivate();

        context.PositionProfileRequiredAssets.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var handler = new GetPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new GetPositionProfileRequest { CompanyId = companyId, Id = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.RequiredAssets);
        var asset = result.Value.RequiredAssets[0];
        Assert.Equal(active.Id, asset.Id);
        Assert.Equal(active.AssetCategoryId, asset.AssetCategoryId);
        Assert.True(asset.IsMandatory);
        Assert.Equal(2, asset.Quantity);
    }

    [Fact]
    public async Task HandleAsync_Returns_EmptyRequiredAssets_When_None_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Manager", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new GetPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new GetPositionProfileRequest { CompanyId = companyId, Id = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.RequiredAssets);
    }

    [Fact]
    public async Task HandleAsync_Excludes_RequiredAssets_From_Other_Profiles()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Designer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var assetForB = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyId, profileB.Id, Guid.NewGuid(), true, 1, Guid.NewGuid(), Now);
        context.PositionProfileRequiredAssets.Add(assetForB);
        await context.SaveChangesAsync();

        var handler = new GetPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new GetPositionProfileRequest { CompanyId = companyId, Id = profileA.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.RequiredAssets);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
