using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.GetWorkEmailSettings;

internal sealed class GetWorkEmailSettingsHandler(CompaniesDbContext dbContext)
{
    public async Task<Result<GetWorkEmailSettingsResponse>> HandleAsync(
        GetWorkEmailSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (settings is null)
        {
            var exists = await dbContext.Companies
                .AsNoTracking()
                .AnyAsync(c => c.Id == request.CompanyId, cancellationToken);

            if (!exists)
                return Result.Failure<GetWorkEmailSettingsResponse>(
                    Error.NotFound($"Company with id '{request.CompanyId}' was not found."));

            settings = CompanySettings.CreateDefault(request.CompanyId, DateTimeOffset.UtcNow);
        }

        var examples = Enum.GetValues<WorkEmailNamingConvention>()
            .Select(convention => new WorkEmailConventionExample(
                convention,
                WorkEmailAddressBuilder.BuildLocalPart(
                    convention,
                    WorkEmailAddressBuilder.SampleFirstName,
                    WorkEmailAddressBuilder.SampleLastName)!))
            .ToList();

        return Result.Success(new GetWorkEmailSettingsResponse(
            settings.CompanyId,
            settings.WorkEmailSuggestionsEnabled,
            settings.WorkEmailPrimaryDomain,
            settings.WorkEmailAdditionalDomains,
            settings.WorkEmailNamingConvention,
            WorkEmailAddressBuilder.SampleFirstName,
            WorkEmailAddressBuilder.SampleLastName,
            examples,
            settings.UpdatedAt,
            settings.Version));
    }
}
