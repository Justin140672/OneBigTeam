using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies;

/// <summary>
/// Server-side enforcement of read-only mode for companies whose trial has expired
/// (<see cref="SubscriptionStatus.TrialExpired"/>). This is the actual gate — UI-level disabling
/// of buttons elsewhere is defense in depth only.
///
/// Mirrors HR.Modules.Identity.RequireTenantMiddleware's shape (constructor-injected
/// RequestDelegate, InvokeAsync short-circuiting with a structured JSON error before calling
/// next) and must be registered after UseIdentityModule so the tenant has already been resolved.
///
/// Only mutation is blocked — "existing data remains accessible" per the epic, so GET/HEAD
/// requests are always allowed through. Subscription/billing/auth endpoints are allow-listed so
/// a read-only company can always resolve the block (start/resume a subscription, sign in, etc).
/// </summary>
internal sealed class ReadOnlyModeMiddleware(RequestDelegate next)
{
    private static readonly string[] AllowListedPathPrefixes =
    [
        "/api/companies/checkout-session",
        "/api/companies/stripe-webhook",
        "/api/companies/subscription/cancel",
        "/api/companies/subscription/resume",
        "/api/companies/subscription/billing-portal",
        "/api/companies/admin/",
        "/api/signup",
        "/api/dev/",
    ];

    public async Task InvokeAsync(
        HttpContext context,
        ISubscriptionStatusReader subscriptionStatusReader,
        ICurrentTenant currentTenant)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (AllowListedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        // ICurrentTenant is the same cross-module contract every Companies handler already uses
        // to resolve the tenant (see GetSubscriptionStatusHandler etc.) — it reads the tenant id
        // Identity's SupabaseCurrentUserResolutionMiddleware already resolved into HttpContext,
        // without Companies referencing Identity's internal types directly (module boundary).
        if (context.User.Identity?.IsAuthenticated == true
            && currentTenant.TenantId is not null
            && Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            var snapshot = await subscriptionStatusReader.GetStatusAsync(companyId, context.RequestAborted);

            if (snapshot.IsReadOnly)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "subscription_read_only",
                    message = "This company's trial has expired and the account is now read-only. " +
                              "Start a subscription to restore full access."
                });
                return;
            }
        }

        await next(context);
    }
}
