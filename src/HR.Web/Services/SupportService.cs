using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace HR.Web.Services;

// Wraps the HR.Modules.Support API surface (submission, thread, staff status changes and the
// staff-only cross-company dashboard). See src/Modules/HR.Modules.Support/Features/*.
public sealed class SupportService(IHttpClientFactory httpClientFactory, ILogger<SupportService> logger)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<List<SupportRequestListItem>?> ListSupportRequestsAsync(
        Guid companyId, string? status = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"api/companies/{companyId}/support/requests";
            if (!string.IsNullOrWhiteSpace(status))
                url += $"?status={Uri.EscapeDataString(status)}";

            return await Http.GetFromJsonAsync<List<SupportRequestListItem>>(url, HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Technical detail logged server-side only — callers surface a generic, non-technical
            // failure message to the end user (see SupportRequestQueue's Failed state).
            logger.LogWarning(ex, "Failed to list support requests for company {CompanyId}.", companyId);
            return null;
        }
    }

    public async Task<SupportRequestDetailModel?> GetSupportRequestAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<SupportRequestDetailModel>(
                $"api/companies/{companyId}/support/requests/{id}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException) { return null; }
    }

    public async Task<SupportDashboardModel?> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<SupportDashboardModel>(
                "api/support/dashboard", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException) { return null; }
    }

    // Returns null on success, or an error message string on failure.
    public async Task<(SubmitSupportRequestResult? Result, string? Error)> SubmitSupportRequestAsync(
        Guid companyId,
        string type,
        string title,
        string description,
        string priority,
        bool includeDiagnostics,
        string? pageUrl,
        string? browser,
        string? appVersion,
        string? correlationId,
        List<string>? recentClientErrors,
        IReadOnlyList<IBrowserFile> files,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(companyId.ToString()), "CompanyId");
            content.Add(new StringContent(type), "Type");
            content.Add(new StringContent(title), "Title");
            content.Add(new StringContent(description), "Description");
            content.Add(new StringContent(priority), "Priority");
            content.Add(new StringContent(includeDiagnostics.ToString()), "IncludeDiagnostics");
            if (!string.IsNullOrWhiteSpace(pageUrl)) content.Add(new StringContent(pageUrl), "PageUrl");
            if (!string.IsNullOrWhiteSpace(browser)) content.Add(new StringContent(browser), "Browser");
            if (!string.IsNullOrWhiteSpace(appVersion)) content.Add(new StringContent(appVersion), "AppVersion");
            if (!string.IsNullOrWhiteSpace(correlationId)) content.Add(new StringContent(correlationId), "CorrelationId");
            if (recentClientErrors is { Count: > 0 })
            {
                foreach (var error in recentClientErrors)
                    content.Add(new StringContent(error), "RecentClientErrors");
            }

            var streams = new List<Stream>();
            try
            {
                foreach (var file in files)
                {
                    var stream = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024, cancellationToken);
                    streams.Add(stream);
                    var fileContent = new StreamContent(stream);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                        string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
                    content.Add(fileContent, "Files", file.Name);
                }

                var response = await Http.PostAsync(
                    $"api/companies/{companyId}/support/requests", content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var created = await response.Content.ReadFromJsonAsync<SubmitSupportRequestResult>(
                        HrApiJsonOptions.Default, cancellationToken);
                    return (created, null);
                }

                return (null, await ReadErrorAsync(response, "Failed to submit request.", cancellationToken));
            }
            finally
            {
                foreach (var stream in streams)
                    await stream.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(UpdateSupportRequestStatusResult? Result, string? Error)> UpdateStatusAsync(
        Guid companyId, Guid id, string status, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PutAsJsonAsync(
                $"api/companies/{companyId}/support/requests/{id}/status",
                new { companyId, id, status }, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var updated = await response.Content.ReadFromJsonAsync<UpdateSupportRequestStatusResult>(
                    HrApiJsonOptions.Default, cancellationToken);
                return (updated, null);
            }

            return (null, await ReadErrorAsync(response, "Failed to update status.", cancellationToken));
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(AddSupportResponseResult? Result, string? Error)> AddResponseAsync(
        Guid companyId,
        Guid id,
        string bodyHtml,
        IReadOnlyList<IBrowserFile> files,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(companyId.ToString()), "CompanyId");
            content.Add(new StringContent(id.ToString()), "Id");
            content.Add(new StringContent(bodyHtml), "BodyHtml");

            var streams = new List<Stream>();
            try
            {
                foreach (var file in files)
                {
                    var stream = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024, cancellationToken);
                    streams.Add(stream);
                    var fileContent = new StreamContent(stream);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                        string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
                    content.Add(fileContent, "Files", file.Name);
                }

                var response = await Http.PostAsync(
                    $"api/companies/{companyId}/support/requests/{id}/responses", content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var created = await response.Content.ReadFromJsonAsync<AddSupportResponseResult>(
                        HrApiJsonOptions.Default, cancellationToken);
                    return (created, null);
                }

                return (null, await ReadErrorAsync(response, "Failed to post reply.", cancellationToken));
            }
            finally
            {
                foreach (var stream in streams)
                    await stream.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (body.TryGetProperty("error", out var errorProp))
                return errorProp.GetString() ?? fallback;
        }
        catch { }

        return $"{fallback} ({(int)response.StatusCode})";
    }
}
