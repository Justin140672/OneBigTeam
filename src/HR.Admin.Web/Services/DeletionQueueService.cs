using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

public sealed class DeletionQueueService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

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
