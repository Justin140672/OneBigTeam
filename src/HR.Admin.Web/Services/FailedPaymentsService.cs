using System.Net.Http.Json;
using HR.Admin.Web.Models;
using HR.SharedKernel;

namespace HR.Admin.Web.Services;

public sealed class FailedPaymentsService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<FailedPaymentsResponse?> GetFailedPaymentsOrNullAsync(
        string? search = null,
        string? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new List<string>();
            search = FormText.OptionalSearch(search);
            if (search is not null)
                query.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrWhiteSpace(statusFilter))
                query.Add($"statusFilter={Uri.EscapeDataString(statusFilter)}");

            var url = "api/companies/admin/failed-payments";
            if (query.Count > 0)
                url += "?" + string.Join("&", query);

            var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<FailedPaymentsResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
