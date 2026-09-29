using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class InvitationBatchService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<(QueueInvitationBatchResponse? Result, string? Error)> QueueBatchAsync(
        Guid companyId, List<Guid> employeeIds, Guid idempotencyKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"api/companies/{companyId}/invitation-batches")
        {
            Content = JsonContent.Create(new QueueInvitationBatchRequest(employeeIds), options: HrApiJsonOptions.Default),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString());

        var result = await ApiResponseReader.ExecuteAsync<QueueInvitationBatchResponse>(
            ct => Http.SendAsync(request, ct), HrApiJsonOptions.Default);

        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to queue invitations."));
    }

    public async Task<(InvitationBatchStatusResponse? Result, string? Error)> GetStatusAsync(
        Guid companyId, Guid batchId)
    {
        var result = await ApiResponseReader.ExecuteAsync<InvitationBatchStatusResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/invitation-batches/{batchId}", ct), HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to load invitation batch status."));
    }

    public async Task<(InvitationBatchStatusResponse? Result, string? Error)> GetLatestAsync(Guid companyId)
    {
        var response = await Http.GetAsync($"api/companies/{companyId}/invitation-batches/latest");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, null);

        var result = await ApiResponseReader.ReadJsonAsync<InvitationBatchStatusResponse>(response, HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to load the latest invitation batch."));
    }

    public async Task<(InvitationBatchStatusResponse? Result, string? Error)> RetryBatchAsync(
        Guid companyId, Guid batchId)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/invitation-batches/{batchId}/retry", new { });
        var result = await ApiResponseReader.ReadJsonAsync<InvitationBatchStatusResponse>(response, HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to retry the failed invitations."));
    }
}
