using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.GetCompany;

internal sealed class GetCompanyHandler
{
    private readonly CompaniesDbContext _dbContext;

    public GetCompanyHandler(CompaniesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetCompanyResponse>> HandleAsync(
        GetCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies
            .Include(currentCompany => currentCompany.Addresses)
            .Include(currentCompany => currentCompany.Branding)
            .AsNoTracking()
            .SingleOrDefaultAsync(company => company.Id == request.Id, cancellationToken);

        if (company is null)
        {
            return Result.Failure<GetCompanyResponse>(Error.NotFound($"Company with id '{request.Id}' was not found."));
        }

        var branding = company.Branding;
        var response = new GetCompanyResponse(
            company.Id,
            company.Name,
            company.IsActive,
            company.CreatedAt,
            company.Addresses
                .Select(address => new GetCompanyAddressResponse(
                    address.Id,
                    address.Type,
                    address.Line1,
                    address.Line2,
                    address.City,
                    address.Region,
                    address.PostalCode,
                    address.CountryCode))
                .OrderBy(address => address.Type)
                .ToArray(),
            new GetCompanyBrandingResponse(
                branding?.PrimaryLogoUrl,
                branding?.SmallLogoUrl,
                branding?.EmailLogoUrl));

        return Result.Success(response);
    }
}