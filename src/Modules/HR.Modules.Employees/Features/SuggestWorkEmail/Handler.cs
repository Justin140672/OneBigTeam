using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.SuggestWorkEmail;

internal sealed class SuggestWorkEmailHandler(
    EmployeesDbContext dbContext,
    ICompanyWorkEmailSettingsReader settingsReader)
{
    public async Task<Result<SuggestWorkEmailResponse>> HandleAsync(
        SuggestWorkEmailRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await settingsReader.GetWorkEmailSettingsAsync(request.CompanyId, cancellationToken);

        var domains = settings.AllDomains;
        if (domains.Count == 0)
            return Result.Success(new SuggestWorkEmailResponse(WorkEmailSuggestionStatus.NotConfigured, null, null, []));

        if (!settings.SuggestionsEnabled)
            return Result.Success(new SuggestWorkEmailResponse(WorkEmailSuggestionStatus.Disabled, null, null, []));

        var requestedDomain = WorkEmailAddressBuilder.NormalizeDomain(request.Domain);
        var selectedDomain = requestedDomain is not null && domains.Contains(requestedDomain)
            ? requestedDomain
            : domains[0];

        var suggestion = WorkEmailAddressBuilder.BuildAddress(
            settings.NamingConvention,
            request.FirstName,
            request.LastName,
            selectedDomain);

        if (suggestion is null)
            return Result.Success(new SuggestWorkEmailResponse(
                WorkEmailSuggestionStatus.NameIncomplete, null, selectedDomain, domains));

        var isTaken = await dbContext.Employees
            .AsNoTracking()
            .AnyAsync(e => e.CompanyId == request.CompanyId && e.WorkEmail == suggestion, cancellationToken);

        return Result.Success(new SuggestWorkEmailResponse(
            isTaken ? WorkEmailSuggestionStatus.Unavailable : WorkEmailSuggestionStatus.Available,
            suggestion,
            selectedDomain,
            domains));
    }
}
