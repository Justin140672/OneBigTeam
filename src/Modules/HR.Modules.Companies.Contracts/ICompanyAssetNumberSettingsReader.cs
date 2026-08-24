namespace HR.Modules.Companies.Contracts;

// Read-only projection of the current asset-number sequence state for a company. Mirrors
// ICompanyEmployeeNumberSettingsReader/EmployeeNumberSequencePreview — see that type's remarks.
public sealed record AssetNumberSequencePreview(string? Prefix, int NextNumber, int MinimumLength);

public interface ICompanyAssetNumberSettingsReader
{
    Task<AssetNumberMode> GetModeAsync(Guid companyId, CancellationToken cancellationToken);

    // Read-only snapshot of the sequence (prefix, next number, minimum length). Callers must not
    // mutate any state from this call; only IAssetNumberGenerator.GenerateNextAsync claims a
    // number and advances the counter.
    Task<AssetNumberSequencePreview> GetSequencePreviewAsync(Guid companyId, CancellationToken cancellationToken);
}
