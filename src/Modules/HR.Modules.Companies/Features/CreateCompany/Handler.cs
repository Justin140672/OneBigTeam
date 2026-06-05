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

        var utcNow = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        var now = new DateTimeOffset(utcNow);

        var company = Company.Create(Guid.NewGuid(), name, slug, now);

        _dbContext.Companies.Add(company);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new CreateCompanyResponse(
            company.Id,
            company.Name,
            company.Slug,
            company.IsActive,
            company.CreatedAt);

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
