using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies;

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
