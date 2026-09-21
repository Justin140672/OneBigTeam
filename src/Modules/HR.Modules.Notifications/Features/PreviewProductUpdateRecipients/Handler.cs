using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Notifications.Domain;
using HR.SharedKernel;

namespace HR.Modules.Notifications.Features.PreviewProductUpdateRecipients;

/// <summary>
/// Customer Release Notifications: computes the "{N} Company Admin users across {M} active
/// customer companies" count the Admin Portal's confirmation dialog shows before SendProductUpdate
/// is allowed to run. Read-only — never writes a notification.
/// </summary>
internal sealed class PreviewProductUpdateRecipientsHandler(
    IActiveCompanyDirectory activeCompanyDirectory,
    ISubscriptionStatusReader subscriptionStatusReader,
    ICompanyAdministratorDirectory companyAdministratorDirectory)
{
    public async Task<Result<PreviewProductUpdateRecipientsResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var recipients = await ProductUpdateRecipientResolver.ResolveAsync(
            activeCompanyDirectory, subscriptionStatusReader, companyAdministratorDirectory, cancellationToken);

        var companyCount = recipients.Select(r => r.CompanyId).Distinct().Count();

        return Result.Success(new PreviewProductUpdateRecipientsResponse(recipients.Count, companyCount));
    }
}
