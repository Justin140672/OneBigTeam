using HR.Web.Models;

namespace HR.Web.Services;

public class SubscriptionService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<GetSubscriptionStatusResponse?> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetSubscriptionStatusResponse>(
                "api/companies/subscription-status", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<CreateCheckoutSessionResponse?> CreateCheckoutSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await Http.PostAsync("api/companies/checkout-session", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<CreateCheckoutSessionResponse>(
            HrApiJsonOptions.Default, cancellationToken);
    }

    public async Task<GetSubscriptionDetailsResponse?> GetDetailsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetSubscriptionDetailsResponse>(
                "api/companies/subscription-details", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<CancelSubscriptionResponse?> CancelAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await Http.PostAsync("api/companies/subscription/cancel", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<CancelSubscriptionResponse>(
            HrApiJsonOptions.Default, cancellationToken);
    }

    public async Task<ResumeSubscriptionResponse?> ResumeAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await Http.PostAsync("api/companies/subscription/resume", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<ResumeSubscriptionResponse>(
            HrApiJsonOptions.Default, cancellationToken);
    }

    public async Task<BillingPortalResponse?> GetBillingPortalAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await Http.PostAsync("api/companies/subscription/billing-portal", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<BillingPortalResponse>(
            HrApiJsonOptions.Default, cancellationToken);
    }
}
