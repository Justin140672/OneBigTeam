using System.Net;
using System.Text;
using System.Web;
using HR.Web.Models;

namespace HR.Web.Services;

// ADM-03: wraps the administrative alerts & incidents inbox API. Same shape as the other
// web services (hrapi IHttpClientFactory client + HrApiJsonOptions.Default).
public class AdministrativeAlertsService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<GetAdministrativeAlertsResponse?> GetAlertsAsync(
        Guid companyId, AdministrativeAlertFilter filter, int pageNumber, int pageSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            if (!string.IsNullOrWhiteSpace(filter.Severity)) query["Severity"] = filter.Severity;
            if (!string.IsNullOrWhiteSpace(filter.Category)) query["Category"] = filter.Category;
            if (!string.IsNullOrWhiteSpace(filter.Status)) query["Status"] = filter.Status;
            if (filter.IsRead is not null) query["IsRead"] = filter.IsRead.Value.ToString();
            if (filter.OccurredFrom is not null) query["OccurredFrom"] = filter.OccurredFrom.Value.ToString("yyyy-MM-dd");
            if (filter.OccurredTo is not null) query["OccurredTo"] = filter.OccurredTo.Value.ToString("yyyy-MM-dd");
            query["PageNumber"] = pageNumber.ToString();
            query["PageSize"] = pageSize.ToString();

            return await Http.GetFromJsonAsync<GetAdministrativeAlertsResponse>(
                $"api/companies/{companyId}/administrative-alerts?{query}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<int?> GetUnreadCountAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetFromJsonAsync<AdministrativeAlertUnreadCountResponse>(
                $"api/companies/{companyId}/administrative-alerts/unread-count", HrApiJsonOptions.Default, cancellationToken);
            return response?.Count;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public Task<AdministrativeAlertActionResult> MarkReadAsync(
        Guid companyId, Guid alertId, CancellationToken cancellationToken = default)
        => PutAsync($"api/companies/{companyId}/administrative-alerts/{alertId}/read", "{}", cancellationToken);

    public Task<AdministrativeAlertActionResult> AcknowledgeAsync(
        Guid companyId, Guid alertId, CancellationToken cancellationToken = default)
        => PutAsync($"api/companies/{companyId}/administrative-alerts/{alertId}/acknowledge", "{}", cancellationToken);

    public Task<AdministrativeAlertActionResult> ResolveAsync(
        Guid companyId, Guid alertId, string? note, CancellationToken cancellationToken = default)
        => PutAsync(
            $"api/companies/{companyId}/administrative-alerts/{alertId}/resolve",
            System.Text.Json.JsonSerializer.Serialize(new ResolveAdministrativeAlertRequest(note), HrApiJsonOptions.Default),
            cancellationToken);

    private async Task<AdministrativeAlertActionResult> PutAsync(
        string url, string json, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await Http.PutAsync(url, content, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.NoContent or HttpStatusCode.OK => AdministrativeAlertActionResult.Success,
                HttpStatusCode.Conflict => AdministrativeAlertActionResult.Conflict,
                HttpStatusCode.NotFound => AdministrativeAlertActionResult.NotFound,
                _ => AdministrativeAlertActionResult.Failed,
            };
        }
        catch (HttpRequestException)
        {
            return AdministrativeAlertActionResult.Failed;
        }
    }
}
