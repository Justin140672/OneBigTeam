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
            .AsNoTracking()
            .SingleOrDefaultAsync(company => company.Id == request.Id, cancellationToken);

        if (company is null)
        {
            return Result.Failure<GetCompanyResponse>(Error.NotFound($"Company with id '{request.Id}' was not found."));
        }

        var response = new GetCompanyResponse(
            company.Id,
            company.Name,
            company.Slug,
            company.IsActive,
            company.CreatedAt);

        return Result.Success(response);
    }
}