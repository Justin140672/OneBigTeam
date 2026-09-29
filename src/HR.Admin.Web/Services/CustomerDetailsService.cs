using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

public sealed class CustomerDetailsService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

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

    public Task<bool> ScheduleDeletionAsync(Guid companyId, string reason, CancellationToken cancellationToken = default) =>
        PostActionAsync(
            $"api/companies/admin/customers/{companyId}/subscription/schedule-deletion",
            new ScheduleDeletionRequest(companyId, reason, CountdownDays: null),
            cancellationToken);

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
