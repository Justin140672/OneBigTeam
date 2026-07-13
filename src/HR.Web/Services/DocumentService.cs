using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Services;

public sealed class DocumentService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<GetExpiringDocumentsResponse?> GetExpiringDocumentsAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetExpiringDocumentsResponse>(
                $"api/companies/{companyId}/documents/expiring", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<EmployeeDocumentListResponse?> ListEmployeeDocumentsAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<EmployeeDocumentListResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/documents", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<DocumentRequestListResponse?> ListDocumentRequestsAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<DocumentRequestListResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/document-requests", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<GetDocumentRequestResponse?> GetDocumentRequestAsync(
        Guid companyId, Guid employeeId, Guid documentRequestId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetDocumentRequestResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/document-requests/{documentRequestId}",
                HrApiJsonOptions.Default, cancellationToken);
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
            return await Http.GetFromJsonAsync<DocumentTypeListResponse>(url, HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<CompanyDocumentCategoryListResponse?> ListCompanyDocumentCategoriesAsync(
        Guid companyId, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = includeInactive
                ? $"api/companies/{companyId}/document-categories?includeInactive=true"
                : $"api/companies/{companyId}/document-categories";
            return await Http.GetFromJsonAsync<CompanyDocumentCategoryListResponse>(url, HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<SharedCompanyDocumentListResponse?> ListSharedCompanyDocumentsAsync(
        Guid companyId,
        string? status = null,
        Guid? categoryId = null,
        DateOnly? reviewDateFrom = null,
        DateOnly? reviewDateTo = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(status))
                query.Add($"status={Uri.EscapeDataString(status)}");
            if (categoryId.HasValue)
                query.Add($"categoryId={categoryId.Value}");
            if (reviewDateFrom.HasValue)
                query.Add($"reviewDateFrom={reviewDateFrom.Value:yyyy-MM-dd}");
            if (reviewDateTo.HasValue)
                query.Add($"reviewDateTo={reviewDateTo.Value:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(search))
                query.Add($"search={Uri.EscapeDataString(search)}");

            var url = $"api/companies/{companyId}/shared-documents";
            if (query.Count > 0)
                url += "?" + string.Join('&', query);

            return await Http.GetFromJsonAsync<SharedCompanyDocumentListResponse>(url, HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<PublishedSharedCompanyDocumentListResponse?> ListPublishedSharedCompanyDocumentsAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<PublishedSharedCompanyDocumentListResponse>(
                $"api/companies/{companyId}/shared-documents/published", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<SharedCompanyDocumentDetailResponse?> GetSharedCompanyDocumentAsync(
        Guid companyId, Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<SharedCompanyDocumentDetailResponse>(
                $"api/companies/{companyId}/shared-documents/{documentId}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<PublishedSharedCompanyDocumentDetailResponse?> GetPublishedSharedCompanyDocumentAsync(
        Guid companyId, Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<PublishedSharedCompanyDocumentDetailResponse>(
                $"api/companies/{companyId}/shared-documents/published/{documentId}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    // Returns null on success, or an error message string on failure.
    public async Task<string?> AcknowledgeSharedCompanyDocumentAsync(
        Guid companyId, Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsync(
                $"api/companies/{companyId}/shared-documents/{documentId}/acknowledge", null, cancellationToken);

            if (response.IsSuccessStatusCode)
                return null;

            try
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (body.TryGetProperty("error", out var errorProp))
                    return errorProp.GetString();
            }
            catch { }

            return $"Acknowledge failed ({(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // Returns null on success, or an error message string on failure.
    public async Task<string?> UpdateSharedCompanyDocumentMetadataAsync(
        Guid companyId,
        Guid documentId,
        string title,
        string? description,
        Guid categoryId,
        DateOnly? effectiveDate,
        DateOnly? reviewDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new UpdateSharedCompanyDocumentMetadataRequest(
                companyId, documentId, title, description, categoryId, effectiveDate, reviewDate);

            var response = await Http.PutAsJsonAsync(
                $"api/companies/{companyId}/shared-documents/{documentId}", request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return null;

            try
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (body.TryGetProperty("error", out var errorProp))
                    return errorProp.GetString();
            }
            catch { }

            return $"Update failed ({(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // Returns null on success, or an error message string on failure.
    public async Task<string?> UpdateSharedCompanyDocumentAudienceAsync(
        Guid companyId,
        Guid documentId,
        IReadOnlyList<Guid> audienceDepartmentIds,
        IReadOnlyList<Guid> audienceLocationIds,
        IReadOnlyList<Guid> audiencePositionProfileIds,
        IReadOnlyList<Guid> audienceEmployeeIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new UpdateSharedCompanyDocumentAudienceRequest(
                companyId, documentId, audienceDepartmentIds, audienceLocationIds, audiencePositionProfileIds, audienceEmployeeIds);

            var response = await Http.PutAsJsonAsync(
                $"api/companies/{companyId}/shared-documents/{documentId}/audience", request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return null;

            try
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (body.TryGetProperty("error", out var errorProp))
                    return errorProp.GetString();
            }
            catch { }

            return $"Update failed ({(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // Relative URL for the download redirect endpoint — bind directly to an <a href> so the
    // browser follows the server-side redirect (and its access check) itself.
    public string GetSharedCompanyDocumentDownloadUrl(Guid companyId, Guid documentId) =>
        $"api/companies/{companyId}/shared-documents/{documentId}/download";

    // Returns null on success, or an error message string on failure.
    public async Task<string?> UploadSharedCompanyDocumentAsync(
        Guid companyId,
        string title,
        string? description,
        Guid categoryId,
        DateOnly? effectiveDate,
        DateOnly? reviewDate,
        IReadOnlyCollection<Guid> audienceDepartmentIds,
        IReadOnlyCollection<Guid> audienceLocationIds,
        IReadOnlyCollection<Guid> audiencePositionProfileIds,
        IReadOnlyCollection<Guid> audienceEmployeeIds,
        bool requiresAcknowledgement,
        IBrowserFile file,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(title), "Title");
            if (!string.IsNullOrWhiteSpace(description))
                content.Add(new StringContent(description), "Description");
            content.Add(new StringContent(categoryId.ToString()), "CategoryId");
            if (effectiveDate.HasValue)
                content.Add(new StringContent(effectiveDate.Value.ToString("yyyy-MM-dd")), "EffectiveDate");
            if (reviewDate.HasValue)
                content.Add(new StringContent(reviewDate.Value.ToString("yyyy-MM-dd")), "ReviewDate");
            foreach (var id in audienceDepartmentIds)
                content.Add(new StringContent(id.ToString()), "AudienceDepartmentIds");
            foreach (var id in audienceLocationIds)
                content.Add(new StringContent(id.ToString()), "AudienceLocationIds");
            foreach (var id in audiencePositionProfileIds)
                content.Add(new StringContent(id.ToString()), "AudiencePositionProfileIds");
            foreach (var id in audienceEmployeeIds)
                content.Add(new StringContent(id.ToString()), "AudienceEmployeeIds");
            content.Add(new StringContent(requiresAcknowledgement.ToString()), "RequiresAcknowledgement");

            await using var stream = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024, cancellationToken);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "File", file.Name);

            var response = await Http.PostAsync(
                $"api/companies/{companyId}/shared-documents",
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
                $"api/companies/{companyId}/employees/{employeeId}/documents/{employeeDocumentId}", HrApiJsonOptions.Default, cancellationToken);
            return doc?.DownloadUrl;
        }
        catch { return null; }
    }
}
