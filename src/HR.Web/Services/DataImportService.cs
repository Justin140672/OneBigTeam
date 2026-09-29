using System.Net.Http.Headers;
using HR.SharedKernel.Http;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Services;

/// <summary>
/// Data-import API client. Every method returns an <see cref="ApiResult{T}"/> so the wizard can tell
/// 401/403/404/409/422/5xx/network/malformed-response failures apart (a failed call is never an empty
/// result), and caller cancellation propagates.
/// </summary>
public sealed class DataImportService(
    HrApiHttpClientFactory httpClientFactory,
    ILogger<DataImportService>? logger = null)
{
    private const long MaxImportFileBytes = 20 * 1024 * 1024;

    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<ApiResult<UploadImportFileResponse>> UploadFileAsync(
        Guid companyId, IBrowserFile file, CancellationToken cancellationToken = default)
    {
        Stream stream;
        try
        {
            stream = file.OpenReadStream(MaxImportFileBytes, cancellationToken);
        }
        catch (IOException)
        {
            return ApiResult<UploadImportFileResponse>.Fail(
                ApiFailureKind.Validation, "The selected file is too large or could not be read. Import files must be 20 MB or smaller.");
        }

        await using (stream)
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("Employee"), "EntityType");

            var fileContent = new StreamContent(stream);
            if (MediaTypeHeaderValue.TryParse(file.ContentType, out var contentType))
                fileContent.Headers.ContentType = contentType;
            content.Add(fileContent, "File", file.Name);

            var result = await ApiResponseReader.ExecuteAsync<UploadImportFileResponse>(
                ct => Http.PostAsync($"api/companies/{companyId}/data-import/sessions", content, ct),
                HrApiJsonOptions.Default, cancellationToken);
            return Require(result, "DataImport.Upload");
        }
    }

    public async Task<ApiResult<ValidateImportSessionResponse>> ValidateSessionAsync(
        Guid companyId, Guid importSessionId, IReadOnlyDictionary<string, string>? columnMapping = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<ValidateImportSessionResponse>(
            ct => Http.PostAsJsonAsync(
                $"api/companies/{companyId}/data-import/sessions/{importSessionId}/validate",
                new { columnMapping }, HrApiJsonOptions.Default, ct),
            HrApiJsonOptions.Default, cancellationToken);
        return Require(result, "DataImport.Validate");
    }

    public Task<ApiResult<GetImportPreviewResponse>> GetPreviewAsync(
        Guid companyId, Guid importSessionId, CancellationToken cancellationToken = default) =>
        GetAsync<GetImportPreviewResponse>(
            "DataImport.Preview",
            $"api/companies/{companyId}/data-import/sessions/{importSessionId}/preview", cancellationToken);

    public async Task<ApiResult<ConfirmImportSessionResponse>> ConfirmSessionAsync(
        Guid companyId, Guid importSessionId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<ConfirmImportSessionResponse>(
            ct => Http.PostAsJsonAsync(
                $"api/companies/{companyId}/data-import/sessions/{importSessionId}/confirm",
                new { }, HrApiJsonOptions.Default, ct),
            HrApiJsonOptions.Default, cancellationToken);
        return Require(result, "DataImport.Confirm");
    }

    public Task<ApiResult<GetImportSessionColumnsResponse>> GetSessionColumnsAsync(
        Guid companyId, Guid importSessionId, CancellationToken cancellationToken = default) =>
        GetAsync<GetImportSessionColumnsResponse>(
            "DataImport.Columns",
            $"api/companies/{companyId}/data-import/sessions/{importSessionId}/columns", cancellationToken);

    public Task<ApiResult<List<ImportSessionSummary>>> ListSessionsAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<List<ImportSessionSummary>>(
            "DataImport.ListSessions", $"api/companies/{companyId}/data-import/sessions", cancellationToken);

    public Task<ApiResult<GetImportSessionResponse>> GetSessionAsync(
        Guid companyId, Guid importSessionId, CancellationToken cancellationToken = default) =>
        GetAsync<GetImportSessionResponse>(
            "DataImport.GetSession",
            $"api/companies/{companyId}/data-import/sessions/{importSessionId}", cancellationToken);

    public async Task<ApiResult<ApiFile>> DownloadErrorReportAsync(
        Guid companyId, Guid importSessionId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteFileAsync(
            ct => Http.GetAsync(
                $"api/companies/{companyId}/data-import/sessions/{importSessionId}/errors/export", ct),
            $"import-errors-{importSessionId}.csv", cancellationToken);
        return result.LogFailure(logger, "DataImport.DownloadErrorReport");
    }

    public async Task<ApiResult<ApiFile>> DownloadTemplateAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteFileAsync(
            ct => Http.GetAsync($"api/companies/{companyId}/data-import/employees/template", ct),
            "employee-import-template.xlsx", cancellationToken);
        return result.LogFailure(logger, "DataImport.DownloadTemplate");
    }

    private async Task<ApiResult<T>> GetAsync<T>(string operation, string url, CancellationToken cancellationToken)
    {
        var result = await ApiResponseReader.ExecuteAsync<T>(
            ct => Http.GetAsync(url, ct), HrApiJsonOptions.Default, cancellationToken);
        return Require(result, operation);
    }

    // A 2xx whose body deserialises to null is a malformed response for these endpoints, never "success".
    private ApiResult<T> Require<T>(ApiResult<T> result, string operation)
    {
        if (result.Success && result.Value is null)
            result = ApiResult<T>.Fail(ApiFailureKind.InvalidResponse, "The server returned an unreadable response.");

        return result.LogFailure(logger, operation);
    }
}
