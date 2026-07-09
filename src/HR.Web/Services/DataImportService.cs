using System.Net.Http.Json;
using System.Text.Json;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Services;

public sealed class DataImportService(IHttpClientFactory httpClientFactory)
{
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
        Guid companyId, Guid importSessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsync(
                $"api/companies/{companyId}/data-import/sessions/{importSessionId}/validate",
                content: null, cancellationToken);

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
            var response = await Http.PostAsync(
                $"api/companies/{companyId}/data-import/sessions/{importSessionId}/confirm",
                content: null, cancellationToken);

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
