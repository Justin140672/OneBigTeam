using System.Net.Http.Json;
using HR.Admin.Web.Models;
using HR.SharedKernel;

namespace HR.Admin.Web.Services;

public sealed class AuditLogService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<AuditLogResponse?> GetAuditLogOrNullAsync(
        Guid? companyId = null,
        string? administratorEmail = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        string? eventType = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new List<string>();
            if (companyId.HasValue)
                query.Add($"companyId={companyId.Value}");
            administratorEmail = FormText.OptionalSearch(administratorEmail);
            if (administratorEmail is not null)
                query.Add($"administratorEmail={Uri.EscapeDataString(administratorEmail)}");
            if (fromDate.HasValue)
                query.Add($"fromDate={Uri.EscapeDataString(fromDate.Value.ToString("O"))}");
            if (toDate.HasValue)
                query.Add($"toDate={Uri.EscapeDataString(toDate.Value.ToString("O"))}");
            if (!string.IsNullOrWhiteSpace(eventType))
                query.Add($"eventType={Uri.EscapeDataString(eventType)}");
            query.Add($"pageNumber={pageNumber}");
            query.Add($"pageSize={pageSize}");

            var url = "api/companies/admin/audit-log?" + string.Join("&", query);

            var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<AuditLogResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
