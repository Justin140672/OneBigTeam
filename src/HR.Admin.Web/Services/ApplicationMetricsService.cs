using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

public sealed class ApplicationMetricsService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    /// <summary>
    /// Returns null when the call fails or the caller isn't authorised (401/403) — same
    /// null-means-"show sign-in/not-authorised state" contract as SystemHealthService. Real
    /// enforcement happens server-side (HR.Api's "platform:admin" policy plus
    /// GetApplicationMetricsHandler's PlatformAdmin:AllowedEmails allow-list); this is UI-side only.
    /// </summary>
    public async Task<ApplicationMetricsResponse?> GetApplicationMetricsOrNullAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync("api/companies/admin/application-metrics", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<ApplicationMetricsResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
