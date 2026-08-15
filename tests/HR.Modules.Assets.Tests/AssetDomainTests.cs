using HR.Modules.Assets.Domain;

namespace HR.Modules.Assets.Tests;

public class AssetDomainTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static Asset CreateAsset() => Asset.Create(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "ASSET-001",
        Guid.NewGuid(),
        "Laptop",
        null,
        null,
        null,
        null,
        null,
        FixedNow);

    [Fact]
    public void Retire_Throws_When_Status_Is_Assigned()
    {
        var asset = CreateAsset();
        asset.MarkAssigned(FixedNow);

        var ex = Assert.Throws<InvalidOperationException>(() => asset.Retire(FixedNow.AddDays(1)));
        Assert.Contains("assigned", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AssetStatus.Assigned, asset.Status);
    }

    [Fact]
    public void Retire_Succeeds_When_Status_Is_Available()
    {
        var asset = CreateAsset();

        asset.Retire(FixedNow.AddDays(1));

        Assert.Equal(AssetStatus.Retired, asset.Status);
    }

    [Fact]
    public void Retire_Succeeds_When_Status_Is_UnderRepair()
    {
        var asset = CreateAsset();
        asset.MarkUnderRepair(FixedNow);

        asset.Retire(FixedNow.AddDays(1));

        Assert.Equal(AssetStatus.Retired, asset.Status);
    }

    [Fact]
    public void Retire_Succeeds_When_Status_Is_Already_Retired()
    {
        var asset = CreateAsset();
        asset.Retire(FixedNow);

        asset.Retire(FixedNow.AddDays(1));

        Assert.Equal(AssetStatus.Retired, asset.Status);
    }
}
