using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Storage;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UploadCompanyLogo;

internal sealed class UploadCompanyLogoHandler
{
    private readonly CompaniesDbContext _dbContext;
    private readonly IBrandingStorage _brandingStorage;
    private readonly IClock _clock;

    public UploadCompanyLogoHandler(
        CompaniesDbContext dbContext,
        IBrandingStorage brandingStorage,
        IClock clock)
    {
        _dbContext = dbContext;
        _brandingStorage = brandingStorage;
        _clock = clock;
    }

    public async Task<Result<UploadCompanyLogoResponse>> HandleAsync(
        UploadCompanyLogoRequest request,
        CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies
            .Include(c => c.Branding)
            .SingleOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (company is null)
        {
            return Result.Failure<UploadCompanyLogoResponse>(
                Error.NotFound($"Company with id '{request.Id}' was not found."));
        }

        var now = _clock.UtcNowOffset();

        var logoUrl = await _brandingStorage.StoreLogoAsync(
            request.Id,
            request.AssetType,
            request.FileName,
            request.ContentType,
            request.FileSizeBytes,
            cancellationToken);

        if (company.Branding is null)
        {
            var branding = CompanyBranding.CreateDefault(company.Id, now);
            branding.SetLogoUrl(request.AssetType, logoUrl, now);
            _dbContext.CompanyBranding.Add(branding);
        }
        else
        {
            company.Branding.SetLogoUrl(request.AssetType, logoUrl, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new UploadCompanyLogoResponse(
            company.Id,
            request.AssetType,
            logoUrl,
            now));
    }
}
