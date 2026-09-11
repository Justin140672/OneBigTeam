using System.Net;
using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

/// <summary>
/// Wraps the /api/notifications/admin/operational-alerts endpoints (all Policies("platform:admin"))
/// via the shared "hrapi" HttpClient. GetXOrNullAsync returns null on any non-2xx / transport
/// failure — same null-means-"show error state" contract as CustomerListService/AuditLogService.
/// ResolveAlertAsync returns a small result type so the page can distinguish
/// success / already-resolved (409) / validation (422) / generic error.
/// </summary>
public sealed class OperationalAlertsService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<OperationalAlertListResponse?> GetAlertsAsync(
        OperationalAlertFilter filter,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new List<string>();
            if (filter.CompanyId.HasValue)
                query.Add($"companyId={filter.CompanyId.Value}");
            if (!string.IsNullOrWhiteSpace(filter.Category))
                query.Add($"category={Uri.EscapeDataString(filter.Category)}");
            if (!string.IsNullOrWhiteSpace(filter.Status))
                query.Add($"status={Uri.EscapeDataString(filter.Status)}");
            query.Add($"page={filter.Page}");
            query.Add($"pageSize={filter.PageSize}");

            var url = "api/notifications/admin/operational-alerts?" + string.Join("&", query);

            var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<OperationalAlertListResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<OperationalAlertDetailResponse?> GetAlertAsync(
        Guid alertId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync($"api/notifications/admin/operational-alerts/{alertId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<OperationalAlertDetailResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<ResolveAlertResult> ResolveAlertAsync(
        Guid alertId,
        string resolutionNote,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/notifications/admin/operational-alerts/{alertId}/resolve",
                new ResolveOperationalAlertRequest(resolutionNote),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<ResolveOperationalAlertResponse>(cancellationToken: cancellationToken);
                return new ResolveAlertResult(ResolveAlertOutcome.Success, body);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Conflict => new ResolveAlertResult(ResolveAlertOutcome.AlreadyResolved, null),
                HttpStatusCode.UnprocessableEntity => new ResolveAlertResult(ResolveAlertOutcome.ValidationFailed, null),
                _ => new ResolveAlertResult(ResolveAlertOutcome.Error, null),
            };
        }
        catch (HttpRequestException)
        {
            return new ResolveAlertResult(ResolveAlertOutcome.Error, null);
        }
    }
}
