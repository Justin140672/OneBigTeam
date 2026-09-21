using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Notifications.Domain;

/// <summary>
/// Customer Release Notifications: resolves the recipient set for a platform-admin-authored
/// ProductUpdate notification. Shared by PreviewProductUpdateRecipients (so the admin sees an
/// accurate N/M count before confirming) and SendProductUpdate (so the actual send uses exactly the
/// same eligibility rules the preview promised) — a single source of truth, not two independently
/// maintained filters.
///
/// Eligibility:
///  - Company must be CompanyStatus.Active (IActiveCompanyDirectory) — excludes companies still in
///    onboarding/verification and companies an operator has deactivated.
///  - Company's subscription must not be expired or cancelled (ISubscriptionStatusReader) — a
///    TrialExpired or Canceled company no longer has application access, so notifying its admins
///    would either bounce (no login) or be misleading. Trial/PastDue/Paused companies still have
///    (possibly read-only) access and remain eligible.
///  - Recipient must be an active (UserProfile.IsActive) Company Administrator
///    (ICompanyAdministratorDirectory) — excludes disabled/revoked accounts.
/// </summary>
internal static class ProductUpdateRecipientResolver
{
    public static async Task<IReadOnlyList<ProductUpdateRecipient>> ResolveAsync(
        IActiveCompanyDirectory activeCompanyDirectory,
        ISubscriptionStatusReader subscriptionStatusReader,
        ICompanyAdministratorDirectory companyAdministratorDirectory,
        CancellationToken cancellationToken)
    {
        var recipients = new List<ProductUpdateRecipient>();

        var activeCompanyIds = await activeCompanyDirectory.GetActiveCompanyIdsAsync(cancellationToken);

        foreach (var companyId in activeCompanyIds)
        {
            var subscription = await subscriptionStatusReader.GetStatusAsync(companyId, cancellationToken);
            if (subscription.Status is SubscriptionStatus.TrialExpired or SubscriptionStatus.Canceled)
                continue;

            var adminEmployeeIds = await companyAdministratorDirectory
                .GetActiveCompanyAdministratorEmployeeIdsAsync(companyId, cancellationToken);

            foreach (var employeeId in adminEmployeeIds)
                recipients.Add(new ProductUpdateRecipient(companyId, employeeId));
        }

        return recipients;
    }
}

internal sealed record ProductUpdateRecipient(Guid CompanyId, Guid EmployeeId);
