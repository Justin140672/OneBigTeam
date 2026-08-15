using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

/// <summary>
/// Wraps the Permanent Deletion Queue endpoints (Customer Lifecycle epic). Modeled exactly on
/// CustomerDetailsService: HttpClientFactory "hrapi" client, GetXxxOrNullAsync returning null on
/// any failure (401/403/404 or a transport error), PostActionAsync returning bool.
/// </summary>
public sealed class DeletionQueueService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    /// <summary>
    /// Returns null when the call fails or the caller isn't authorised (401/403) — same
    /// null-means-"show error state" contract as CustomerDetailsService.
    /// </summary>
    public async Task<DeletionQueueResponse?> GetDeletionQueueOrNullAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync("api/companies/admin/deletion-queue", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<DeletionQueueResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Shared execution for the schedule/cancel/execute deletion actions below. Returns true on a
    /// successful (2xx) response; false on any failure (401/403/404/400 or a transport error), same
    /// contract as CustomerDetailsService.PostActionAsync.
    /// </summary>
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

    public Task<bool> ScheduleDeletionAsync(Guid companyId, string reason, int? countdownDays = null, CancellationToken cancellationToken = default) =>
        PostActionAsync(
            $"api/companies/admin/customers/{companyId}/subscription/schedule-deletion",
            new ScheduleDeletionRequest(companyId, reason, countdownDays),
            cancellationToken);

    public Task<bool> CancelDeletionAsync(Guid companyId, string reason, CancellationToken cancellationToken = default) =>
        PostActionAsync(
            $"api/companies/admin/customers/{companyId}/subscription/cancel-deletion",
            new CancelDeletionRequest(companyId, reason),
            cancellationToken);

    public Task<bool> ExecuteDeletionAsync(Guid companyId, string reason, CancellationToken cancellationToken = default) =>
        PostActionAsync(
            $"api/companies/admin/customers/{companyId}/subscription/execute-deletion",
            new ExecuteDeletionRequest(companyId, reason),
            cancellationToken);
}
