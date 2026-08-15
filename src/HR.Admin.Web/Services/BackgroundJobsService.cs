using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

public sealed class BackgroundJobsService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    /// <summary>
    /// Returns null when the call fails or the caller isn't authorised (401/403) — same
    /// null-means-"show sign-in/not-authorised state" contract as FailedPaymentsService. Real
    /// enforcement happens server-side (HR.Api's "platform:admin" policy plus
    /// ListBackgroundJobsHandler's PlatformAdmin:AllowedEmails allow-list); this is UI-side only.
    /// </summary>
    public async Task<BackgroundJobsResponse?> GetBackgroundJobsOrNullAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync("api/companies/admin/background-jobs", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<BackgroundJobsResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Requeues a failed job. Returns true only on a confirmed successful retry.</summary>
    public async Task<bool> RetryJobAsync(string jobId, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/admin/background-jobs/{Uri.EscapeDataString(jobId)}/retry",
                new { JobId = jobId, Reason = reason },
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
