using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

public sealed class CustomerSupportViewService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    /// <summary>
    /// Returns null when the call fails, the caller isn't authorised (401/403), or the company
    /// isn't found (404) — same null-means-"show error state" contract as
    /// CustomerDetailsService.GetCustomerDetailsOrNullAsync.
    /// </summary>
    public async Task<CustomerSupportViewResponse?> GetSupportViewOrNullAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync($"api/companies/admin/customers/{companyId}/support-view", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<CustomerSupportViewResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
