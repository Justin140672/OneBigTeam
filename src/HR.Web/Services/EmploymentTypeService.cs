using HR.Web.Models;

namespace HR.Web.Services;

public class EmploymentTypeService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListEmploymentTypesResponse?> ListEmploymentTypesAsync(Guid companyId, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/employment-types";
            if (!includeInactive) url += "?isActive=true";
            return await Http.GetFromJsonAsync<ListEmploymentTypesResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreateEmploymentTypeResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateEmploymentTypeRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/employment-types", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateEmploymentTypeResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "An employment type with that name already exists.");
        }

        return (null, "Failed to create employment type.");
    }

    public async Task<(UpdateEmploymentTypeResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid id, UpdateEmploymentTypeRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/employment-types/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateEmploymentTypeResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "An employment type with that name already exists.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Employment type not found.");

        return (null, "Failed to update employment type.");
    }

    public async Task<string?> DeactivateAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/employment-types/{id}");

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Employment type not found.";

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return body?.Error ?? "Failed to deactivate employment type.";
    }

    private sealed record ErrorEnvelope(string? Error);
}
