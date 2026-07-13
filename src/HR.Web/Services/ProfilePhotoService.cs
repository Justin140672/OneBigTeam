using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Services;

public sealed class ProfilePhotoService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    // ── Self-service ────────────────────────────────────────────────────────

    public async Task<GetMyProfilePhotoResponse?> GetMyProfilePhotoAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetMyProfilePhotoResponse>(
                $"api/companies/{companyId}/employees/me/profile-photo", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    // Returns null on success, or an error message string on failure.
    public async Task<string?> UploadMyProfilePhotoAsync(
        Guid companyId, IBrowserFile file, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            await using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024, cancellationToken);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "File", file.Name);

            var response = await Http.PostAsync(
                $"api/companies/{companyId}/employees/me/profile-photo",
                content, cancellationToken);

            if (response.IsSuccessStatusCode)
                return null;

            return await ReadErrorAsync(response, "Upload", cancellationToken);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public async Task<bool> CancelPendingProfilePhotoAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.DeleteAsync(
                $"api/companies/{companyId}/employees/me/profile-photo/pending", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── HR-facing ────────────────────────────────────────────────────────────

    // Returns null on success, or an error message string on failure.
    public async Task<string?> UploadEmployeeProfilePhotoAsync(
        Guid companyId, Guid employeeId, IBrowserFile file, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            await using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024, cancellationToken);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "File", file.Name);

            var response = await Http.PostAsync(
                $"api/companies/{companyId}/employees/{employeeId}/profile-photo",
                content, cancellationToken);

            if (response.IsSuccessStatusCode)
                return null;

            return await ReadErrorAsync(response, "Upload", cancellationToken);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public async Task<GetPendingProfilePhotoResponse?> GetPendingProfilePhotoAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/employees/{employeeId}/profile-photo/pending", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<GetPendingProfilePhotoResponse>(HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<GetPendingProfilePhotoByIdResponse?> GetPendingProfilePhotoByIdAsync(
        Guid companyId, Guid pendingPhotoId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/profile-photo/pending/{pendingPhotoId}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<GetPendingProfilePhotoByIdResponse>(HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<GetEmployeeProfilePhotoResponse?> GetEmployeeProfilePhotoAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/employees/{employeeId}/profile-photo", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<GetEmployeeProfilePhotoResponse>(HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    // Returns null on success, or an error message string on failure.
    public async Task<string?> ApproveProfilePhotoAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/profile-photo/pending/approve",
                new { }, cancellationToken);

            if (response.IsSuccessStatusCode)
                return null;

            return await ReadErrorAsync(response, "Approve", cancellationToken);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // Returns null on success, or an error message string on failure.
    public async Task<string?> RejectProfilePhotoAsync(
        Guid companyId, Guid employeeId, string? rejectionReason, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new { rejectionReason };
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/profile-photo/pending/reject",
                body, cancellationToken);

            if (response.IsSuccessStatusCode)
                return null;

            return await ReadErrorAsync(response, "Reject", cancellationToken);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (body.TryGetProperty("error", out var errorProp))
                return errorProp.GetString() ?? $"{action} failed ({(int)response.StatusCode}).";
        }
        catch { }

        return $"{action} failed ({(int)response.StatusCode}).";
    }
}
