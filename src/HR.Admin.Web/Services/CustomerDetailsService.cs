using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

public sealed class CustomerDetailsService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    /// <summary>
    /// Returns null when the call fails, the caller isn't authorised (401/403), or the company
    /// isn't found (404) — same null-means-"show error state" contract as
    /// CustomerListService.GetCustomersOrNullAsync. The page shows one generic error banner and
    /// does not distinguish between these cases.
    /// </summary>
    public async Task<CustomerDetailsResponse?> GetCustomerDetailsOrNullAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync($"api/companies/admin/customers/{companyId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<CustomerDetailsResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Shared execution for every Subscription Management admin action below. Returns true on a
    /// successful (2xx) response; false on any failure (401/403/404/400 or a transport error) so
    /// the calling page can show a single generic error banner, same null/false-means-"show error"
    /// contract as GetCustomerDetailsOrNullAsync above.
    /// </summary>
    private async Task<bool> PostActionAsync<TRequest>(
        string path, TRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(path, request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public Task<bool> ExtendTrialAsync(Guid companyId, DateTimeOffset newTrialExpiresAt, string reason, CancellationToken cancellationToken = default) =>
        PostActionAsync(
            $"api/companies/admin/customers/{companyId}/subscription/extend-trial",
            new ExtendTrialRequest(newTrialExpiresAt, reason),
            cancellationToken);

    public Task<bool> CancelSubscriptionAsync(Guid companyId, string reason, CancellationToken cancellationToken = default) =>
        PostActionAsync(
            $"api/companies/admin/customers/{companyId}/subscription/cancel",
            new SubscriptionActionRequest(reason),
            cancellationToken);

    public Task<bool> ReinstateSubscriptionAsync(Guid companyId, string reason, CancellationToken cancellationToken = default) =>
        PostActionAsync(
            $"api/companies/admin/customers/{companyId}/subscription/reinstate",
            new SubscriptionActionRequest(reason),
            cancellationToken);

    public Task<bool> ForceReadOnlyAsync(Guid companyId, string reason, CancellationToken cancellationToken = default) =>
        PostActionAsync(
            $"api/companies/admin/customers/{companyId}/subscription/force-read-only",
            new SubscriptionActionRequest(reason),
            cancellationToken);

    public Task<bool> ResumeServiceAsync(Guid companyId, string reason, CancellationToken cancellationToken = default) =>
        PostActionAsync(
            $"api/companies/admin/customers/{companyId}/subscription/resume-service",
            new SubscriptionActionRequest(reason),
            cancellationToken);

    /// <summary>
    /// Schedules a permanent deletion (Customer Lifecycle epic) using the server's default 30-day
    /// countdown — no countdownDays supplied here, matching the "keep the UI simple" convention.
    /// </summary>
    public Task<bool> ScheduleDeletionAsync(Guid companyId, string reason, CancellationToken cancellationToken = default) =>
        PostActionAsync(
            $"api/companies/admin/customers/{companyId}/subscription/schedule-deletion",
            new ScheduleDeletionRequest(companyId, reason, CountdownDays: null),
            cancellationToken);

    /// <summary>
    /// Generates a time-boxed support-session access token for "Login as customer" (Support epic).
    /// Returns null on any failure (401/403/404/400 or a transport error), same null-means-"show
    /// error" contract as GetCustomerDetailsOrNullAsync above.
    /// </summary>
    public async Task<GenerateSupportSessionResponse?> GenerateSupportSessionAsync(
        Guid companyId, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/admin/customers/{companyId}/support-session",
                new SubscriptionActionRequest(reason),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<GenerateSupportSessionResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Revokes a previously generated support session. No request body — route-bound, same
    /// no-body-POST convention as the endpoint contract. Returns null on any failure.
    /// </summary>
    public async Task<RevokeSupportSessionResponse?> RevokeSupportSessionAsync(
        Guid supportSessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsync(
                $"api/companies/admin/support-sessions/{supportSessionId}/revoke",
                content: null,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<RevokeSupportSessionResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns null when the call fails, the caller isn't authorised (401/403), or the company
    /// isn't found (404) — same null-means-"show error state" contract as
    /// GetCustomerDetailsOrNullAsync above. Each successful call also causes the API to persist a
    /// new billing snapshot row, which is why this page shows a growing "history" table over time.
    /// </summary>
    public async Task<CustomerBillingBreakdownResponse?> GetBillingBreakdownOrNullAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync($"api/companies/admin/customers/{companyId}/billing-breakdown", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<CustomerBillingBreakdownResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns null when the call fails, the caller isn't authorised (401/403), or the company
    /// isn't found (404) — same null-means-"show error state" contract as
    /// GetCustomerDetailsOrNullAsync above. A non-null response with an empty Invoices list is a
    /// valid, distinct outcome (see StripeConfigured/HasStripeCustomer on the response) — that is
    /// not an error and must not be treated as one by the page.
    /// </summary>
    public async Task<CustomerBillingHistoryResponse?> GetBillingHistoryOrNullAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync($"api/companies/admin/customers/{companyId}/billing-history", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<CustomerBillingHistoryResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
