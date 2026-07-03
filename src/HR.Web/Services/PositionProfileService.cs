using HR.Web.Models;

namespace HR.Web.Services;

public class PositionProfileService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListPositionProfilesResponse?> ListPositionProfilesAsync(Guid companyId, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/position-profiles";
            if (includeInactive) url += "?includeInactive=true";
            return await Http.GetFromJsonAsync<ListPositionProfilesResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetPositionProfileResponse?> GetPositionProfileAsync(Guid companyId, Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetPositionProfileResponse>(
                $"api/companies/{companyId}/position-profiles/{id}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreatePositionProfileResponse? Profile, string? Error)> CreatePositionProfileAsync(
        Guid companyId, CreatePositionProfileRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/position-profiles", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreatePositionProfileResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A position profile with that title already exists.");
        }

        return (null, "Failed to create position profile.");
    }

    public async Task<ListRequiredDocumentsResponse?> ListRequiredDocumentsAsync(
        Guid companyId, Guid positionProfileId)
    {
        try
        {
            return await Http.GetFromJsonAsync<ListRequiredDocumentsResponse>(
                $"api/companies/{companyId}/position-profiles/{positionProfileId}/required-documents", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(bool Success, string? Error)> AddRequiredDocumentAsync(
        Guid companyId, Guid positionProfileId, AddRequiredDocumentToProfileRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/position-profiles/{positionProfileId}/required-documents", request);

        if (response.IsSuccessStatusCode)
            return (true, null);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (false, body?.Error ?? "This document type is already required.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (false, "Position profile or document type not found.");

        return (false, "Failed to add required document.");
    }

    public async Task<bool> RemoveRequiredDocumentAsync(
        Guid companyId, Guid positionProfileId, Guid requiredDocumentId)
    {
        try
        {
            var response = await Http.DeleteAsync(
                $"api/companies/{companyId}/position-profiles/{positionProfileId}/required-documents/{requiredDocumentId}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool Success, string? Error)> UpdatePositionProfileAsync(
        Guid companyId, Guid id, UpdatePositionProfileRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/position-profiles/{id}", request);

        if (response.IsSuccessStatusCode)
            return (true, null);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (false, body?.Error ?? "A conflict occurred.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (false, "Position profile not found.");

        return (false, "Failed to save position profile.");
    }

    private sealed record ErrorEnvelope(string? Error);
}
