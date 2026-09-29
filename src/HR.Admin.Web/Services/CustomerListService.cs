using System.Net.Http.Json;
using HR.Admin.Web.Models;
using HR.SharedKernel;

namespace HR.Admin.Web.Services;

public sealed class CustomerListService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<CustomerListResponse?> GetCustomersOrNullAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            search = FormText.OptionalSearch(search);
            var url = search is null
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
