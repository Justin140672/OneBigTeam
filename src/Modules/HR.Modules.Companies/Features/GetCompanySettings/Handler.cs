using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.GetCompanySettings;

internal sealed class GetCompanySettingsHandler(CompaniesDbContext dbContext)
{
    public async Task<Result<GetCompanySettingsResponse>> HandleAsync(
        GetCompanySettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (settings is null)
        {
            // Company exists but has no customised settings — return defaults
            var exists = await dbContext.Companies
                .AsNoTracking()
                .AnyAsync(c => c.Id == request.CompanyId, cancellationToken);

            if (!exists)
                return Result.Failure<GetCompanySettingsResponse>(
                    Error.NotFound($"Company with id '{request.CompanyId}' was not found."));

            var defaults = Domain.CompanySettings.CreateDefault(request.CompanyId, DateTimeOffset.UtcNow);
            return Result.Success(new GetCompanySettingsResponse(
                defaults.CompanyId, defaults.TimeZone, defaults.Locale,
                defaults.PostcodeRegex, defaults.TelephoneRegex, defaults.MobileRegex,
                defaults.UpdatedAt));
        }

        return Result.Success(new GetCompanySettingsResponse(
            settings.CompanyId, settings.TimeZone, settings.Locale,
            settings.PostcodeRegex, settings.TelephoneRegex, settings.MobileRegex,
            settings.UpdatedAt));
    }
}
