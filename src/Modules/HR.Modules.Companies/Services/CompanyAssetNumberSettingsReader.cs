using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyAssetNumberSettingsReader(CompaniesDbContext dbContext) : ICompanyAssetNumberSettingsReader
{
    public async Task<AssetNumberMode> GetModeAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        return settings?.AssetNumberMode ?? AssetNumberMode.Manual;
    }

    public async Task<AssetNumberSequencePreview> GetSequencePreviewAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (settings is null)
        {
            throw new InvalidOperationException(
                $"Cannot preview asset numbers: company '{companyId}' has no company_settings row.");
        }

        return new AssetNumberSequencePreview(
            settings.AssetNumberPrefix, settings.NextAssetNumber, settings.AssetNumberMinimumLength);
    }
}
