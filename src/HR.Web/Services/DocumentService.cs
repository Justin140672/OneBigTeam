using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Services;

public sealed class DocumentService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<EmployeeDocumentListResponse?> ListEmployeeDocumentsAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<EmployeeDocumentListResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/documents", cancellationToken);
        }
        catch { return null; }
    }

    public async Task<DocumentRequestListResponse?> ListDocumentRequestsAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<DocumentRequestListResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/document-requests",
                cancellationToken);
        }
        catch { return null; }
    }

    public async Task<DocumentTypeListResponse?> ListDocumentTypesAsync(
        Guid companyId, bool employeeUploadOnly = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = employeeUploadOnly
                ? $"api/companies/{companyId}/document-types?employeeUploadOnly=true"
                : $"api/companies/{companyId}/document-types";
            return await Http.GetFromJsonAsync<DocumentTypeListResponse>(url, cancellationToken);
        }
        catch { return null; }
    }

    // Returns null on success, or an error message string on failure.
    public async Task<string?> UploadEmployeeDocumentAsync(
        Guid companyId,
        Guid employeeId,
        string title,
        string? description,
        Guid documentTypeId,
        DateOnly? issueDate,
        DateOnly? expiryDate,
        IBrowserFile file,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(title), "Title");
            if (!string.IsNullOrWhiteSpace(description))
                content.Add(new StringContent(description), "Description");
            content.Add(new StringContent(documentTypeId.ToString()), "DocumentTypeId");
            if (issueDate.HasValue)
                content.Add(new StringContent(issueDate.Value.ToString("yyyy-MM-dd")), "IssueDate");
            if (expiryDate.HasValue)
                content.Add(new StringContent(expiryDate.Value.ToString("yyyy-MM-dd")), "ExpiryDate");

            await using var stream = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024, cancellationToken);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "File", file.Name);

            var response = await Http.PostAsync(
                $"api/companies/{companyId}/employees/{employeeId}/documents",
                content, cancellationToken);

            if (response.IsSuccessStatusCode)
                return null;

            // Try to surface the structured error message from the API.
            try
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (body.TryGetProperty("error", out var errorProp))
                    return errorProp.GetString();
            }
            catch { }

            return $"Upload failed ({(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public async Task<string?> UploadRequestedDocumentAsync(
        Guid companyId,
        Guid employeeId,
        Guid documentRequestId,
        string title,
        string? description,
        DateOnly? issueDate,
        DateOnly? expiryDate,
        IBrowserFile file,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(title), "Title");
            if (!string.IsNullOrWhiteSpace(description))
                content.Add(new StringContent(description), "Description");
            if (issueDate.HasValue)
                content.Add(new StringContent(issueDate.Value.ToString("yyyy-MM-dd")), "IssueDate");
            if (expiryDate.HasValue)
                content.Add(new StringContent(expiryDate.Value.ToString("yyyy-MM-dd")), "ExpiryDate");

            await using var stream = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024, cancellationToken);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "File", file.Name);

            var response = await Http.PostAsync(
                $"api/companies/{companyId}/employees/{employeeId}/document-requests/{documentRequestId}/upload",
                content, cancellationToken);

            if (response.IsSuccessStatusCode)
                return null;

            try
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (body.TryGetProperty("error", out var errorProp))
                    return errorProp.GetString();
            }
            catch { }

            return $"Upload failed ({(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public async Task<bool> CancelDocumentRequestAsync(
        Guid companyId, Guid employeeId, Guid documentRequestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.DeleteAsync(
                $"api/companies/{companyId}/employees/{employeeId}/document-requests/{documentRequestId}",
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string?> RequestDocumentAsync(
        Guid companyId,
        Guid employeeId,
        Guid documentTypeId,
        DateOnly? dueDate,
        bool isMandatory,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new { documentTypeId, dueDate = dueDate?.ToString("yyyy-MM-dd"), isMandatory, notes };
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/document-requests",
                body, cancellationToken);

            if (response.IsSuccessStatusCode)
                return null;

            try
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (json.TryGetProperty("error", out var errorProp))
                    return errorProp.GetString();
            }
            catch { }

            return $"Request failed ({(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public async Task<bool> DeleteEmployeeDocumentAsync(
        Guid companyId, Guid employeeId, Guid employeeDocumentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.DeleteAsync(
                $"api/companies/{companyId}/employees/{employeeId}/documents/{employeeDocumentId}",
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string?> GetDownloadUrlAsync(
        Guid companyId, Guid employeeId, Guid employeeDocumentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var doc = await Http.GetFromJsonAsync<EmployeeDocumentDetailResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/documents/{employeeDocumentId}",
                cancellationToken);
            return doc?.DownloadUrl;
        }
        catch { return null; }
    }
}
