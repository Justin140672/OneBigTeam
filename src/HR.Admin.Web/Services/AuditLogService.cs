using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

public sealed class AuditLogService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    /// <summary>
    /// Returns null when the call fails or the caller isn't authorised (401/403) — same
    /// null-means-"show sign-in/not-authorised state" contract as CustomerListService/
    /// FailedPaymentsService. Real enforcement happens server-side (HR.Api's "platform:admin"
    /// policy plus GetAuditLogHandler's PlatformAdmin:AllowedEmails allow-list); this is UI-side
    /// only.
    /// </summary>
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
            if (!string.IsNullOrWhiteSpace(administratorEmail))
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
