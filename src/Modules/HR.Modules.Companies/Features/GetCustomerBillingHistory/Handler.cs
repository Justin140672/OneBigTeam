using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Features.GetCustomerBillingHistory;

/// <summary>
/// Same defense-in-depth allow-list gate as GetCustomerDetailsHandler/GetCustomerBillingBreakdownHandler
/// (see their remarks). Unlike the billing breakdown, this feature never invents or computes local
/// data — it either calls the real Stripe Invoices API for the customer's StripeCustomerId, or
/// reports plainly (via StripeConfigured/HasStripeCustomer) why it can't.
/// </summary>
internal sealed class GetCustomerBillingHistoryHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IStripeGateway stripeGateway,
    IOptions<StripeOptions> stripeOptions)
{
    private readonly CompaniesDbContext _dbContext = dbContext;

    public async Task<Result<GetCustomerBillingHistoryResponse>> HandleAsync(
        GetCustomerBillingHistoryRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<GetCustomerBillingHistoryResponse>(
                Error.Unauthorized("This account is not authorised to view platform-wide customer data."));
        }

        var companyExists = await _dbContext.Companies
            .AsNoTracking()
            .AnyAsync(c => c.Id == request.CompanyId, cancellationToken);

        if (!companyExists)
        {
            return Result.Failure<GetCustomerBillingHistoryResponse>(
                Error.NotFound($"Company with id '{request.CompanyId}' was not found."));
        }

        var stripeConfigured = !string.IsNullOrWhiteSpace(stripeOptions.Value.SecretKey);

        var subscription = await _dbContext.CustomerSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        var hasStripeCustomer = !string.IsNullOrWhiteSpace(subscription?.StripeCustomerId);

        if (!stripeConfigured || !hasStripeCustomer)
        {
            return Result.Success(new GetCustomerBillingHistoryResponse(
                request.CompanyId,
                stripeConfigured,
                hasStripeCustomer,
                []));
        }

        var stripeInvoices = await stripeGateway.ListInvoicesAsync(
            subscription!.StripeCustomerId!,
            cancellationToken);

        // Employee count per invoice is not tracked by Stripe (checkout always uses a fixed
        // line-item quantity of 1 regardless of headcount), so it is approximated from the most
        // recent CustomerBillingSnapshot recorded at or before the invoice date, when one exists.
        var snapshots = await _dbContext.CustomerBillingSnapshots
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId)
            .OrderBy(s => s.ComputedAt)
            .Select(s => new { s.ComputedAt, s.ChargeableEmployees })
            .ToListAsync(cancellationToken);

        var invoices = stripeInvoices
            .OrderByDescending(i => i.InvoiceDate)
            .Select(invoice =>
            {
                var closestSnapshot = snapshots
                    .Where(s => s.ComputedAt <= invoice.InvoiceDate)
                    .OrderByDescending(s => s.ComputedAt)
                    .FirstOrDefault();

                return new BillingHistoryInvoiceDto(
                    invoice.Id,
                    invoice.InvoiceDate,
                    invoice.Amount,
                    invoice.Currency,
                    closestSnapshot?.ChargeableEmployees,
                    invoice.Status,
                    invoice.PaidAt,
                    invoice.HostedInvoiceUrl);
            })
            .ToList();

        return Result.Success(new GetCustomerBillingHistoryResponse(
            request.CompanyId,
            stripeConfigured,
            hasStripeCustomer,
            invoices));
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
