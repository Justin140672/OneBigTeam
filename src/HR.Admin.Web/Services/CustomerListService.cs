using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

public sealed class CustomerListService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    /// <summary>
    /// Returns null when the call fails or the caller isn't authorised (401/403) — same
    /// null-means-"show sign-in/not-authorised state" contract as
    /// CustomerDashboardService.GetDashboardOrNullAsync. Real enforcement happens server-side
    /// (HR.Api's "platform:admin" policy plus ListCustomersHandler's PlatformAdmin:AllowedEmails
    /// allow-list); this is UI-side only.
    /// </summary>
    public async Task<CustomerListResponse?> GetCustomersOrNullAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = string.IsNullOrWhiteSpace(search)
                ? "api/companies/admin/customers"
                : $"api/companies/admin/customers?search={Uri.EscapeDataString(search)}";

            var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<CustomerListResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
