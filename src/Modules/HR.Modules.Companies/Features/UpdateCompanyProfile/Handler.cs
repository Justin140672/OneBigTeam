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
            .SingleOrDefaultAsync(company => company.Id == request.Id, cancellationToken);

        if (company is null)
        {
            return Result.Failure<UpdateCompanyProfileResponse>(Error.NotFound($"Company with id '{request.Id}' was not found."));
        }

        var name = request.Name.Trim();
        var utcNow = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        var now = new DateTimeOffset(utcNow);

        company.Update(name, now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new UpdateCompanyProfileResponse(
            company.Id,
            company.Name,
            company.Slug,
            company.IsActive,
            company.CreatedAt,
            company.UpdatedAt);

        return Result.Success(response);
    }
}