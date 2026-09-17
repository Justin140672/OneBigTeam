using System.Net.Http.Headers;
using HR.SharedKernel;
using HR.SharedKernel.Http;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace HR.Web.Services;

// Wraps the HR.Modules.Support API surface (submission, thread, staff status changes and the
// staff-only cross-company dashboard). See src/Modules/HR.Modules.Support/Features/*.
public sealed class SupportService(HrApiHttpClientFactory httpClientFactory, ILogger<SupportService> logger)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<List<SupportRequestListItem>?> ListSupportRequestsAsync(
        Guid companyId, string? status = null, CancellationToken cancellationToken = default)
    {
        var url = $"api/companies/{companyId}/support/requests";
        if (!string.IsNullOrWhiteSpace(status))
            url += $"?status={Uri.EscapeDataString(status)}";

        var result = await ApiResponseReader.ExecuteAsync<List<SupportRequestListItem>>(
            ct => Http.GetAsync(url, ct), HrApiJsonOptions.Default, cancellationToken);

        if (!result.Success)
        {
            // Technical detail logged server-side only — callers surface a generic, non-technical
            // failure message to the end user (see SupportRequestQueue's Failed state).
            logger.LogWarning("Failed to list support requests for company {CompanyId}: {Error}", companyId, result.Error);
            return null;
        }

        return result.Value;
    }

    public async Task<SupportRequestDetailModel?> GetSupportRequestAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<SupportRequestDetailModel>(
            ct => Http.GetAsync($"api/companies/{companyId}/support/requests/{id}", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    public async Task<SupportDashboardModel?> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<SupportDashboardModel>(
            ct => Http.GetAsync("api/support/dashboard", ct), HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
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
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(companyId.ToString()), "CompanyId");
        content.Add(new StringContent(type), "Type");
        content.Add(new StringContent(FormText.Required(title)), "Title");
        content.Add(new StringContent(FormText.Required(description)), "Description");
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

            var result = await ApiResponseReader.ExecuteAsync<SubmitSupportRequestResult>(
                ct => Http.PostAsync($"api/companies/{companyId}/support/requests", content, ct),
                HrApiJsonOptions.Default, cancellationToken);
            return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to submit request."));
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

    // Ticket 16: status-change write path removed. Per SupportRequestQueue.razor's own product
    // copy ("Ticket status can only be changed by support staff in the Admin app"), status editing
    // is exclusively an HR.Admin.Web capability — see SupportRequestAdminService in HR.Admin.Web.
    // This method previously had no callers anywhere in HR.Web (confirmed via repo-wide grep
    // before removal) and is deleted rather than kept as unused dead code. Read-side Version
    // display (SupportRequestListItem/SupportRequestDetailModel.Version) is retained — HR.Web
    // still needs to display the current version/"last changed" state even though it never writes it.

    public async Task<(AddSupportResponseResult? Result, string? Error)> AddResponseAsync(
        Guid companyId,
        Guid id,
        string bodyHtml,
        IReadOnlyList<IBrowserFile> files,
        CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(companyId.ToString()), "CompanyId");
        content.Add(new StringContent(id.ToString()), "Id");
        // bodyHtml is rich/formatted editor content — not run through FormText, whitespace
        // and markup here are meaningful and normalized (if at all) by the rich-text editor itself.
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

            var result = await ApiResponseReader.ExecuteAsync<AddSupportResponseResult>(
                ct => Http.PostAsync($"api/companies/{companyId}/support/requests/{id}/responses", content, ct),
                HrApiJsonOptions.Default, cancellationToken);
            return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to post reply."));
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }
}
