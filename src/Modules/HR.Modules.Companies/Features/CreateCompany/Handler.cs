using System.Text;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.CreateCompany;

internal sealed class CreateCompanyHandler
{
    private readonly CompaniesDbContext _dbContext;
    private readonly IClock _clock;

    public CreateCompanyHandler(CompaniesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<CreateCompanyResponse>> HandleAsync(
        CreateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? GenerateSlug(name)
            : request.Slug.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(slug))
        {
            return Result.Failure<CreateCompanyResponse>(Error.Validation("Company slug could not be generated from the provided name."));
        }

        var slugExists = await _dbContext.Companies
            .AnyAsync(company => company.Slug == slug, cancellationToken);

        if (slugExists)
        {
            return Result.Failure<CreateCompanyResponse>(Error.Conflict($"A company with slug '{slug}' already exists."));
        }

        var now = _clock.UtcNowOffset();

        var company = Company.Create(Guid.NewGuid(), name, slug, now);
        var registeredOfficeRequest = request.Addresses
            .SingleOrDefault(address => address.Type == CompanyAddressType.RegisteredOffice);
        var tradingAddressRequest = request.Addresses
            .SingleOrDefault(address => address.Type == CompanyAddressType.TradingAddress);

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

        company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

        _dbContext.Companies.Add(company);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new CreateCompanyResponse(
            company.Id,
            company.Name,
            company.Slug,
            company.IsActive,
            company.CreatedAt,
            company.Addresses
                .Select(address => new CreateCompanyAddressResponse(
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

    private static string GenerateSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(name.Length);
        var previousDash = false;

        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Append(character);
                previousDash = false;
                continue;
            }

            if (!previousDash)
            {
                buffer.Append('-');
                previousDash = true;
            }
        }

        return buffer
            .ToString()
            .Trim('-');
    }
}
