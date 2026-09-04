using HR.Web.Models;

namespace HR.Web.Services;

public class PositionProfileService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListPositionProfilesResponse?> ListPositionProfilesAsync(
        Guid companyId, bool includeInactive = false, string? search = null, int? pageSize = null)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            if (includeInactive) query["includeInactive"] = "true";
            if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
            if (pageSize is > 0) query["pageSize"] = pageSize.Value.ToString();

            var url = $"api/companies/{companyId}/position-profiles";
            if (query.Count > 0) url += $"?{query}";

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

    public async Task<ListRequiredAssetsResponse?> ListRequiredAssetsAsync(
        Guid companyId, Guid positionProfileId)
    {
        try
        {
            return await Http.GetFromJsonAsync<ListRequiredAssetsResponse>(
                $"api/companies/{companyId}/position-profiles/{positionProfileId}/required-assets", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(bool Success, string? Error)> AddRequiredAssetAsync(
        Guid companyId, Guid positionProfileId, AddRequiredAssetToProfileRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/position-profiles/{positionProfileId}/required-assets", request);

        if (response.IsSuccessStatusCode)
            return (true, null);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (false, body?.Error ?? "This asset category is already required.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (false, "Position profile or asset category not found.");

        return (false, "Failed to add required asset.");
    }

    public async Task<bool> RemoveRequiredAssetAsync(
        Guid companyId, Guid positionProfileId, Guid requiredAssetId)
    {
        try
        {
            var response = await Http.DeleteAsync(
                $"api/companies/{companyId}/position-profiles/{positionProfileId}/required-assets/{requiredAssetId}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ListOnboardingTemplatesForProfileResponse?> ListOnboardingTemplatesForProfileAsync(
        Guid companyId, Guid positionProfileId)
    {
        try
        {
            return await Http.GetFromJsonAsync<ListOnboardingTemplatesForProfileResponse>(
                $"api/companies/{companyId}/position-profiles/{positionProfileId}/onboarding-templates", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(bool Success, string? Error)> AddOnboardingTemplateAsync(
        Guid companyId, Guid positionProfileId, AddOnboardingTemplateToProfileRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/position-profiles/{positionProfileId}/onboarding-templates", request);

        if (response.IsSuccessStatusCode)
            return (true, null);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (false, body?.Error ?? "This onboarding template is already assigned.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (false, "Position profile or onboarding template not found.");

        return (false, "Failed to add onboarding template.");
    }

    public async Task<bool> RemoveOnboardingTemplateAsync(
        Guid companyId, Guid positionProfileId, Guid assignmentId)
    {
        try
        {
            var response = await Http.DeleteAsync(
                $"api/companies/{companyId}/position-profiles/{positionProfileId}/onboarding-templates/{assignmentId}");
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

    public async Task<string?> DeactivatePositionProfileAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/position-profiles/{id}");

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Position profile not found.";

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return body?.Error ?? "Failed to deactivate position profile.";
    }

    public async Task<ListPositionRoleDefaultsResponse?> ListPositionRoleDefaultsAsync(Guid companyId)
    {
        try
        {
            return await Http.GetFromJsonAsync<ListPositionRoleDefaultsResponse>(
                $"api/companies/{companyId}/positions/role-defaults", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(bool Success, string? Error)> SetPositionRoleDefaultsAsync(
        Guid companyId, Guid positionProfileId, SetPositionRoleDefaultsRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/positions/{positionProfileId}/role-defaults", request);

        if (response.IsSuccessStatusCode)
            return (true, null);

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return (false, "You are not permitted to grant one or more of the selected roles.");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (false, "Position profile not found.");

        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (false, body?.Error ?? "Failed to save inherited roles.");
        }
        catch
        {
            // Non-JSON / empty error body (e.g. a bare 500) — don't let a deserialisation failure
            // bubble out as an unhandled exception.
            return (false, "Failed to save inherited roles.");
        }
    }

    private sealed record ErrorEnvelope(string? Error);
}
