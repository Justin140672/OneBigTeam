using System.Net.Http.Headers;
using HR.SharedKernel.Http;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Services;

/// <summary>
/// Profile-photo API client. Reads return <see cref="ApiResult{T}"/>: a 404 is <see cref="ApiFailureKind.NotFound"/>
/// ("no such photo") and is therefore distinguishable from 401/403/5xx/network/malformed-response failures.
/// Mutations never report success when the API call failed, and caller cancellation propagates.
/// </summary>
public sealed class ProfilePhotoService(
    HrApiHttpClientFactory httpClientFactory,
    ILogger<ProfilePhotoService>? logger = null)
{
    private const long MaxPhotoBytes = 5 * 1024 * 1024;

    private HttpClient Http => httpClientFactory.CreateClient();

    private async Task<ApiResult<T>> GetAsync<T>(string operation, string url, CancellationToken cancellationToken)
    {
        var result = await ApiResponseReader.ExecuteAsync<T>(
            ct => Http.GetAsync(url, ct), HrApiJsonOptions.Default, cancellationToken);
        return result.LogFailure(logger, operation);
    }

    public Task<ApiResult<GetMyProfilePhotoResponse>> GetMyProfilePhotoAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<GetMyProfilePhotoResponse>(
            "ProfilePhoto.GetMine", $"api/companies/{companyId}/employees/me/profile-photo", cancellationToken);

    public Task<ApiResult<Unit>> UploadMyProfilePhotoAsync(
        Guid companyId, IBrowserFile file, CancellationToken cancellationToken = default) =>
        UploadAsync("ProfilePhoto.UploadMine", $"api/companies/{companyId}/employees/me/profile-photo", file, cancellationToken);

    public Task<ApiResult<Unit>> UploadEmployeeProfilePhotoAsync(
        Guid companyId, Guid employeeId, IBrowserFile file, CancellationToken cancellationToken = default) =>
        UploadAsync(
            "ProfilePhoto.UploadEmployee", $"api/companies/{companyId}/employees/{employeeId}/profile-photo",
            file, cancellationToken);

    public async Task<ApiResult<Unit>> CancelPendingProfilePhotoAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteNoContentAsync(
            ct => Http.DeleteAsync($"api/companies/{companyId}/employees/me/profile-photo/pending", ct),
            cancellationToken);
        return result.LogFailure(logger, "ProfilePhoto.CancelPending");
    }

    public Task<ApiResult<GetPendingProfilePhotoResponse>> GetPendingProfilePhotoAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default) =>
        GetAsync<GetPendingProfilePhotoResponse>(
            "ProfilePhoto.GetPending",
            $"api/companies/{companyId}/employees/{employeeId}/profile-photo/pending", cancellationToken);

    public Task<ApiResult<GetPendingProfilePhotoByIdResponse>> GetPendingProfilePhotoByIdAsync(
        Guid companyId, Guid pendingPhotoId, CancellationToken cancellationToken = default) =>
        GetAsync<GetPendingProfilePhotoByIdResponse>(
            "ProfilePhoto.GetPendingById",
            $"api/companies/{companyId}/profile-photo/pending/{pendingPhotoId}", cancellationToken);

    public Task<ApiResult<GetEmployeeProfilePhotoResponse>> GetEmployeeProfilePhotoAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default) =>
        GetAsync<GetEmployeeProfilePhotoResponse>(
            "ProfilePhoto.GetEmployee",
            $"api/companies/{companyId}/employees/{employeeId}/profile-photo", cancellationToken);

    public async Task<ApiResult<Unit>> ApproveProfilePhotoAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteNoContentAsync(
            ct => Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/profile-photo/pending/approve", new { }, ct),
            cancellationToken);
        return result.LogFailure(logger, "ProfilePhoto.Approve");
    }

    public async Task<ApiResult<Unit>> RejectProfilePhotoAsync(
        Guid companyId, Guid employeeId, string? rejectionReason, CancellationToken cancellationToken = default)
    {
        var body = new { rejectionReason };
        var result = await ApiResponseReader.ExecuteNoContentAsync(
            ct => Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/profile-photo/pending/reject", body, ct),
            cancellationToken);
        return result.LogFailure(logger, "ProfilePhoto.Reject");
    }

    private async Task<ApiResult<Unit>> UploadAsync(
        string operation, string url, IBrowserFile file, CancellationToken cancellationToken)
    {
        Stream stream;
        try
        {
            stream = file.OpenReadStream(MaxPhotoBytes, cancellationToken);
        }
        catch (IOException)
        {
            // Oversized or unreadable selection: a problem with the user's input, not a server fault.
            return ApiResult<Unit>.Fail(
                ApiFailureKind.Validation, "The selected file is too large or could not be read. Photos must be 5 MB or smaller.");
        }

        await using (stream)
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(stream);
            if (MediaTypeHeaderValue.TryParse(file.ContentType, out var contentType))
                fileContent.Headers.ContentType = contentType;
            content.Add(fileContent, "File", file.Name);

            var result = await ApiResponseReader.ExecuteNoContentAsync(
                ct => Http.PostAsync(url, content, ct), cancellationToken);
            return result.LogFailure(logger, operation);
        }
    }
}
