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

        if (string.IsNullOrEmpty(settings.PrimaryDomain))
            return Result.Success(new SuggestWorkEmailResponse(WorkEmailSuggestionStatus.NotConfigured, null));

        if (!settings.SuggestionsEnabled)
            return Result.Success(new SuggestWorkEmailResponse(WorkEmailSuggestionStatus.Disabled, null));

        var suggestion = WorkEmailAddressBuilder.BuildAddress(
            settings.NamingConvention,
            request.FirstName,
            request.LastName,
            settings.PrimaryDomain);

        if (suggestion is null)
            return Result.Success(new SuggestWorkEmailResponse(
                WorkEmailSuggestionStatus.NameIncomplete, null));

        var isTaken = await dbContext.Employees
            .AsNoTracking()
            .AnyAsync(e => e.CompanyId == request.CompanyId && e.WorkEmail == suggestion, cancellationToken);

        return Result.Success(new SuggestWorkEmailResponse(
            isTaken ? WorkEmailSuggestionStatus.Unavailable : WorkEmailSuggestionStatus.Available,
            suggestion));
    }
}
