using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Features.GetFailedPayments;

/// <summary>
/// Same defense-in-depth allow-list gate as ListCustomersHandler/GetCustomerBillingHistoryHandler
/// (see their remarks). Platform-wide (not scoped to one customer) — queries Stripe once for all
/// failed/unpaid invoices across the account (see IStripeGateway.ListFailedInvoicesAsync remarks for
/// why this is two account-wide calls rather than N per-customer calls), then joins back to local
/// Company/CustomerSubscription data by StripeCustomerId. "Last successful payment" is looked up
/// per-customer, but only for the (expected small) set of customers currently appearing in the
/// failed-payments list, not for every customer in the account.
/// </summary>
internal sealed class GetFailedPaymentsHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IStripeGateway stripeGateway,
    IOptions<StripeOptions> stripeOptions)
{
    private readonly CompaniesDbContext _dbContext = dbContext;

    public async Task<Result<GetFailedPaymentsResponse>> HandleAsync(
        GetFailedPaymentsRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<GetFailedPaymentsResponse>(
                Error.Unauthorized("This account is not authorised to view platform-wide customer data."));
        }

        var stripeConfigured = !string.IsNullOrWhiteSpace(stripeOptions.Value.SecretKey);
        if (!stripeConfigured)
        {
            return Result.Success(new GetFailedPaymentsResponse(StripeConfigured: false, []));
        }

        var failedInvoices = await stripeGateway.ListFailedInvoicesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.StatusFilter))
        {
            failedInvoices = failedInvoices
                .Where(i => string.Equals(i.Status, request.StatusFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var stripeCustomerIds = failedInvoices
            .Select(i => i.StripeCustomerId)
            .Distinct()
            .ToList();

        var subscriptions = await _dbContext.CustomerSubscriptions
            .AsNoTracking()
            .Where(s => s.StripeCustomerId != null && stripeCustomerIds.Contains(s.StripeCustomerId!))
            .ToListAsync(cancellationToken);

        var subscriptionByStripeCustomerId = subscriptions
            .Where(s => s.StripeCustomerId is not null)
            .ToDictionary(s => s.StripeCustomerId!, s => s);

        var companyIds = subscriptions.Select(s => s.CompanyId).ToList();
        var companies = await _dbContext.Companies
            .AsNoTracking()
            .Where(c => companyIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        var companyById = companies.ToDictionary(c => c.Id, c => c);

        // Bounded by the number of distinct failing customers, not every customer in the account —
        // see class remarks.
        var lastPaidInvoiceByStripeCustomerId = new Dictionary<string, StripeInvoiceSummary?>();
        foreach (var stripeCustomerId in stripeCustomerIds)
        {
            lastPaidInvoiceByStripeCustomerId[stripeCustomerId] =
                await stripeGateway.GetMostRecentPaidInvoiceAsync(stripeCustomerId, cancellationToken);
        }

        var search = request.Search?.Trim();

        var items = failedInvoices
            .Where(invoice => subscriptionByStripeCustomerId.ContainsKey(invoice.StripeCustomerId))
            .Select(invoice =>
            {
                var subscription = subscriptionByStripeCustomerId[invoice.StripeCustomerId];
                companyById.TryGetValue(subscription.CompanyId, out var company);
                lastPaidInvoiceByStripeCustomerId.TryGetValue(invoice.StripeCustomerId, out var lastPaid);

                return new FailedPaymentDto(
                    subscription.CompanyId,
                    company?.Name ?? "(unknown company)",
                    subscription.Status.ToString(),
                    invoice.Id,
                    invoice.Status,
                    invoice.OutstandingAmount,
                    invoice.Currency,
                    invoice.InvoiceDate,
                    invoice.NextPaymentAttempt,
                    lastPaid?.PaidAt,
                    lastPaid?.Amount,
                    invoice.HostedInvoiceUrl);
            })
            .Where(dto => string.IsNullOrWhiteSpace(search)
                || dto.CompanyName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(dto => dto.InvoiceDate)
            .ToList();

        return Result.Success(new GetFailedPaymentsResponse(StripeConfigured: true, items));
    }

    private bool IsAllowListedPlatformAdmin()
    {
        var email = currentUser.Email;
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var allowedEmails = configuration.GetSection("PlatformAdmin:AllowedEmails").Get<string[]>()
            ?? [];

        return allowedEmails.Any(allowed =>
            string.Equals(allowed, email, StringComparison.OrdinalIgnoreCase));
    }
}
