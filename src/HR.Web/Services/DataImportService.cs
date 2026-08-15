using System.Net.Http.Json;
using System.Text.Json;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Services;

public sealed class DataImportService(IHttpClientFactory httpClientFactory)
{
    /// <summary>Sentinel returned as the error string from <see cref="GetSessionAsync"/> when the
    /// session doesn't exist, so callers can show a "not found" message instead of a generic error.</summary>
    public const string NotFoundSentinel = "NotFound";

    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<(UploadImportFileResponse? Result, string? Error)> UploadFileAsync(
        Guid companyId, IBrowserFile file, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("Employee"), "EntityType");

            await using var stream = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024, cancellationToken);
            var fileContent = new StreamContent(stream);
            if (!string.IsNullOrWhiteSpace(file.ContentType))
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "File", file.Name);

            var response = await Http.PostAsync(
                $"api/companies/{companyId}/data-import/sessions", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var created = await response.Content.ReadFromJsonAsync<UploadImportFileResponse>(
                    HrApiJsonOptions.Default, cancellationToken);
                return (created, null);
            }

            return (null, await ExtractErrorAsync(response, "Upload failed.", cancellationToken));
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(ValidateImportSessionResponse? Result, string? Error)> ValidateSessionAsync(
        Guid companyId, Guid importSessionId, IReadOnlyDictionary<string, string>? columnMapping = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // FastEndpoints rejects a truly bodyless POST (no Content-Type header at all) with
            // 415 Unsupported Media Type, even though ValidateImportSessionRequest's properties
            // are all route-bound — so post an empty JSON object rather than a null body.
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/data-import/sessions/{importSessionId}/validate",
                new { columnMapping }, HrApiJsonOptions.Default, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ValidateImportSessionResponse>(
                    HrApiJsonOptions.Default, cancellationToken);
                return (result, null);
            }

            return (null, await ExtractErrorAsync(response, "Validation failed.", cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(GetImportPreviewResponse? Result, string? Error)> GetPreviewAsync(
        Guid companyId, Guid importSessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/data-import/sessions/{importSessionId}/preview", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<GetImportPreviewResponse>(
                    HrApiJsonOptions.Default, cancellationToken);
                return (result, null);
            }

            return (null, await ExtractErrorAsync(response, "Failed to load preview.", cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(ConfirmImportSessionResponse? Result, string? Error)> ConfirmSessionAsync(
        Guid companyId, Guid importSessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Same bodyless-POST 415 issue as ValidateSessionAsync above — post an empty JSON
            // object so FastEndpoints sees a valid Content-Type instead of none at all.
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/data-import/sessions/{importSessionId}/confirm",
                new { }, HrApiJsonOptions.Default, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ConfirmImportSessionResponse>(
                    HrApiJsonOptions.Default, cancellationToken);
                return (result, null);
            }

            return (null, await ExtractErrorAsync(response, "Confirm failed.", cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(GetImportSessionColumnsResponse? Result, string? Error)> GetSessionColumnsAsync(
        Guid companyId, Guid importSessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/data-import/sessions/{importSessionId}/columns", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<GetImportSessionColumnsResponse>(
                    HrApiJsonOptions.Default, cancellationToken);
                return (result, null);
            }

            return (null, await ExtractErrorAsync(response, "Failed to load detected columns.", cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(List<ImportSessionSummary>? Result, string? Error)> ListSessionsAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/data-import/sessions", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<ImportSessionSummary>>(
                    HrApiJsonOptions.Default, cancellationToken);
                return (result, null);
            }

            return (null, await ExtractErrorAsync(response, "Failed to load import history.", cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(GetImportSessionResponse? Result, string? Error)> GetSessionAsync(
        Guid companyId, Guid importSessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/data-import/sessions/{importSessionId}", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<GetImportSessionResponse>(
                    HrApiJsonOptions.Default, cancellationToken);
                return (result, null);
            }

            // Callers distinguish "not found" from other failures by checking for this exact
            // sentinel value, so a missing session can be shown as a friendly message rather
            // than a generic error banner.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, NotFoundSentinel);

            return (null, await ExtractErrorAsync(response, "Failed to load import session.", cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(byte[]? Bytes, string FileName, string? Error)> DownloadErrorReportAsync(
        Guid companyId, Guid importSessionId, CancellationToken cancellationToken = default)
    {
        var fallbackFileName = $"import-errors-{importSessionId}.csv";
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/data-import/sessions/{importSessionId}/errors/export", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var fileName = GetAttachmentFileName(response, fallbackFileName);
                return (bytes, fileName, null);
            }

            return (null, fallbackFileName, await ExtractErrorAsync(response, "Failed to download error report.", cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return (null, fallbackFileName, ex.Message);
        }
    }

    public async Task<(byte[]? Bytes, string FileName, string? Error)> DownloadTemplateAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        const string fallbackFileName = "employee-import-template.xlsx";
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/data-import/employees/template", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var fileName = GetAttachmentFileName(response, fallbackFileName);
                return (bytes, fileName, null);
            }

            return (null, fallbackFileName, await ExtractErrorAsync(response, "Failed to download template.", cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return (null, fallbackFileName, ex.Message);
        }
    }

    private static string GetAttachmentFileName(HttpResponseMessage response, string fallback)
    {
        try
        {
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName;
            if (!string.IsNullOrWhiteSpace(fileName))
                return fileName.Trim('"');
        }
        catch { }

        return fallback;
    }

    private static async Task<string> ExtractErrorAsync(
        HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
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
