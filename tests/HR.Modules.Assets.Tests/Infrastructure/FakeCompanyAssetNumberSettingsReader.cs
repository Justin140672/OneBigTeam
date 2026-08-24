using HR.Modules.Companies.Contracts;

namespace HR.Modules.Assets.Tests.Infrastructure;

internal sealed class FakeCompanyAssetNumberSettingsReader(AssetNumberMode mode = AssetNumberMode.Manual)
    : ICompanyAssetNumberSettingsReader
{
    public Task<AssetNumberMode> GetModeAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(mode);

    public Task<AssetNumberSequencePreview> GetSequencePreviewAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(new AssetNumberSequencePreview(null, 1, 1));
}

internal sealed class FakeAssetNumberGenerator(Func<int, string>? format = null) : IAssetNumberGenerator
{
    private int _counter;

    public Task<string> GenerateNextAsync(Guid companyId, CancellationToken cancellationToken)
    {
        _counter++;
        var value = format is null ? $"AUTO-{_counter:D5}" : format(_counter);
        return Task.FromResult(value);
    }
}
