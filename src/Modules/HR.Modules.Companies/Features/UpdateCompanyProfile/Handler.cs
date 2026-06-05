using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateCompanyProfile;

internal sealed class UpdateCompanyProfileHandler
{
    private readonly CompaniesDbContext _dbContext;
    private readonly IClock _clock;

    public UpdateCompanyProfileHandler(CompaniesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<UpdateCompanyProfileResponse>> HandleAsync(
        UpdateCompanyProfileRequest request,
        CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies
            .Include(currentCompany => currentCompany.Addresses)
            .Include(currentCompany => currentCompany.Branding)
            .SingleOrDefaultAsync(currentCompany => currentCompany.Id == request.Id, cancellationToken);

        if (company is null)
        {
            return Result.Failure<UpdateCompanyProfileResponse>(
                Error.NotFound($"Company with id '{request.Id}' was not found."));
        }

        var utcNow = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        var now = new DateTimeOffset(utcNow);
        var registeredOfficeRequest = request.Addresses
            .SingleOrDefault(address => address.Type == CompanyAddressType.RegisteredOffice);
        var tradingAddressRequest = request.Addresses
            .SingleOrDefault(address => address.Type == CompanyAddressType.TradingAddress);

        company.Update(request.Name.Trim(), now);

        if (registeredOfficeRequest is not null)
        {
            var address = CompanyAddress.Create(
                Guid.NewGuid(),
                company.Id,
                CompanyAddressType.RegisteredOffice,
                registeredOfficeRequest.Line1.Trim(),
                string.IsNullOrWhiteSpace(registeredOfficeRequest.Line2) ? null : registeredOfficeRequest.Line2.Trim(),
                registeredOfficeRequest.City.Trim(),
                string.IsNullOrWhiteSpace(registeredOfficeRequest.Region) ? null : registeredOfficeRequest.Region.Trim(),
                string.IsNullOrWhiteSpace(registeredOfficeRequest.PostalCode) ? null : registeredOfficeRequest.PostalCode.Trim(),
                registeredOfficeRequest.CountryCode.Trim().ToUpperInvariant(),
                now);

            company.SetAddress(address, now);

            var tradingSource = tradingAddressRequest ?? registeredOfficeRequest;
            var tradingAddress = CompanyAddress.Create(
                Guid.NewGuid(),
                company.Id,
                CompanyAddressType.TradingAddress,
                tradingSource.Line1.Trim(),
                string.IsNullOrWhiteSpace(tradingSource.Line2) ? null : tradingSource.Line2.Trim(),
                tradingSource.City.Trim(),
                string.IsNullOrWhiteSpace(tradingSource.Region) ? null : tradingSource.Region.Trim(),
                string.IsNullOrWhiteSpace(tradingSource.PostalCode) ? null : tradingSource.PostalCode.Trim(),
                tradingSource.CountryCode.Trim().ToUpperInvariant(),
                now);

            company.SetAddress(tradingAddress, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var branding = company.Branding ?? CompanyBranding.CreateDefault(company.Id, company.CreatedAt);

        var response = new UpdateCompanyProfileResponse(
            company.Id,
            company.Name,
            company.Slug,
            company.IsActive,
            company.CreatedAt,
            company.UpdatedAt,
            new CompanyBrandingMetadataResponse(
                branding.PrimaryLogoUrl,
                branding.SmallLogoUrl,
                branding.EmailLogoUrl,
                branding.PrimaryColor,
                branding.SecondaryColor,
                branding.AccentColor,
                branding.UpdatedAt),
            company.Addresses
                .Select(address => new UpdateCompanyAddressResponse(
                    address.Id,
                    address.Type,
                    address.Line1,
                    address.Line2,
                    address.City,
                    address.Region,
                    address.PostalCode,
                    address.CountryCode))
                .OrderBy(address => address.Type)
                .ToArray());

        return Result.Success(response);
    }
}
