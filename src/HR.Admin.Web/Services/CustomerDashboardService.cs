using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

public sealed class CustomerDashboardService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    /// <summary>
    /// Returns null when the call fails or the caller isn't authorised (401/403) — callers treat a
    /// null result as "show the sign-in/not-authorised state", not as a crash. Real enforcement of
    /// who can see this data happens server-side (HR.Api's "platform:admin" policy plus
    /// GetCustomerDashboardHandler's PlatformAdmin:AllowedEmails allow-list); this is UI-side only.
    /// </summary>
    public async Task<CustomerDashboardResponse?> GetDashboardOrNullAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync("api/companies/admin/customer-dashboard", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<CustomerDashboardResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
