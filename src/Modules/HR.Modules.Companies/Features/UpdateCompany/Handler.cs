using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateCompany;

internal sealed class UpdateCompanyHandler
{
    private readonly CompaniesDbContext _dbContext;
    private readonly IClock _clock;

    public UpdateCompanyHandler(CompaniesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<UpdateCompanyResponse>> HandleAsync(
        UpdateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies
            .Include(c => c.Addresses)
            .SingleOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (company is null)
        {
            return Result.Failure<UpdateCompanyResponse>(
                Error.NotFound($"Company with id '{request.Id}' was not found."));
        }

        var now = _clock.UtcNowOffset();

        // --- Profile ---
        company.Update(request.Name.Trim(), now);

        // --- Addresses ---
        var registeredOfficeRequest = request.Addresses
            .SingleOrDefault(address => address.Type == CompanyAddressType.RegisteredOffice);
        var tradingAddressRequest = request.Addresses
            .SingleOrDefault(address => address.Type == CompanyAddressType.TradingAddress);

        if (registeredOfficeRequest is not null)
        {
            company.SetAddress(CompanyAddress.Create(
                Guid.NewGuid(),
                company.Id,
                CompanyAddressType.RegisteredOffice,
                registeredOfficeRequest.Line1.Trim(),
                string.IsNullOrWhiteSpace(registeredOfficeRequest.Line2) ? null : registeredOfficeRequest.Line2.Trim(),
                registeredOfficeRequest.City.Trim(),
                string.IsNullOrWhiteSpace(registeredOfficeRequest.Region) ? null : registeredOfficeRequest.Region.Trim(),
                string.IsNullOrWhiteSpace(registeredOfficeRequest.PostalCode) ? null : registeredOfficeRequest.PostalCode.Trim(),
                registeredOfficeRequest.CountryCode.Trim().ToUpperInvariant(),
                now), now);

            var tradingSource = tradingAddressRequest ?? registeredOfficeRequest;
            company.SetAddress(CompanyAddress.Create(
                Guid.NewGuid(),
                company.Id,
                CompanyAddressType.TradingAddress,
                tradingSource.Line1.Trim(),
                string.IsNullOrWhiteSpace(tradingSource.Line2) ? null : tradingSource.Line2.Trim(),
                tradingSource.City.Trim(),
                string.IsNullOrWhiteSpace(tradingSource.Region) ? null : tradingSource.Region.Trim(),
                string.IsNullOrWhiteSpace(tradingSource.PostalCode) ? null : tradingSource.PostalCode.Trim(),
                tradingSource.CountryCode.Trim().ToUpperInvariant(),
                now), now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateCompanyResponse(
            company.Id,
            company.Name,
            company.IsActive,
            company.CreatedAt,
            company.UpdatedAt,
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
                .ToArray()));
    }
}
