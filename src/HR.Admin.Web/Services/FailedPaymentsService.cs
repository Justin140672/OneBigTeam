using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

public sealed class FailedPaymentsService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    /// <summary>
    /// Returns null when the call fails or the caller isn't authorised (401/403) — same
    /// null-means-"show sign-in/not-authorised state" contract as CustomerListService. Real
    /// enforcement happens server-side (HR.Api's "platform:admin" policy plus
    /// GetFailedPaymentsHandler's PlatformAdmin:AllowedEmails allow-list); this is UI-side only.
    /// </summary>
    public async Task<FailedPaymentsResponse?> GetFailedPaymentsOrNullAsync(
        string? search = null,
        string? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
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
